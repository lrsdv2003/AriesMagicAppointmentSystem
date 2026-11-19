using System.Reflection;
using AriesMagicAppointmentSystem.Controllers;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

internal static class OwnerArchivesRegression
{
    public static async Task RunAsync(ApplicationDbContext db,Action<bool,string> check)
    {
        check(typeof(TrashHistoryController).GetCustomAttributes<AuthorizeAttribute>()
            .Any(x=>x.Roles=="Owner"),"Archives controller remains Owner-only");
        check(typeof(TrashHistoryController).GetMethods().Any(m=>m.Name=="Details") &&
              typeof(TrashHistoryController).GetMethods().All(m=>m.Name is not ("Restore" or "Delete")),
              "Archives preserves read-only Details endpoint used by History without adding mutation routes");

        var key="ArchivesReadOnly"+Guid.NewGuid().ToString("N");
        var client=new User{FullName=key,Email=key+"@example.test",Role="Client"};
        var package=new Service{Name="Archived "+key,Price=11000,IsArchived=true,DurationInHours=2};
        var baseDate=DateTime.UtcNow.Date.AddDays(-180);
        var records=Enumerable.Range(0,8).Select(i=>{
            var day=baseDate.AddDays(i);
            return new Booking{
                Client=client,Service=package,PackageName=package.Name,
                Status=i%3==0?BookingStatus.Cancelled:i%3==1?BookingStatus.Declined:BookingStatus.Expired,
                EventDate=day.AddDays(15),StartTime=day.AddDays(15).AddHours(10),
                EndTime=day.AddDays(15).AddHours(12),
                CreatedAt=day,ArchivedAt=i==0?null:baseDate.AddDays(i+25),
                TrashReason=TrashReason.BookingRequestExpired,
                PartyVenue="Archive test venue",EventType="Birthday",FinalPrice=11000
            };
        }).ToList();
        db.Bookings.AddRange(records);
        await db.SaveChangesAsync();
        var audit=new SystemActivity{
            Type=SystemActivityType.BookingArchived,
            Description="Persistent archive actor metadata",
            PerformedByUserId="archive-actor",
            PerformedByUserName="Historical Archive Operator",
            AffectedRecordId=records[7].Id.ToString(),AffectedRecordType="Booking",
            MetadataJson="{\"Trace\":\"preserve-actor-reference\"}",CreatedAt=baseDate.AddDays(60)
        };
        db.SystemActivities.Add(audit);
        await db.SaveChangesAsync();
        try{
            var service=new TrashHistoryService(db);
            var newest=await service.GetTrashHistoryAsync(new(){Search=key});
            check(newest.TotalCount==8&&newest.Bookings.Count==5&&newest.TotalPages==2&&
                  newest.Filters.Page==1&&newest.Filters.PageSize==5&&
                  newest.Bookings.Select(x=>x.Id).SequenceEqual(records.AsEnumerable().Reverse().Take(5).Select(x=>x.Id)),
                  "Archives retrieves five newest records by recorded ArchivedAt with five-record pagination");
            check(newest.Bookings.All(x=>x.BookingStatus is BookingStatus.Cancelled or BookingStatus.Declined or BookingStatus.Expired) &&
                  newest.Bookings.All(x=>x.StartTime==TimeSpan.FromHours(10) && x.EndTime==TimeSpan.FromHours(12)) &&
                  newest.Bookings.All(x=>x.EventDate==records.Single(b=>b.Id==x.Id).EventDate),
                  "Five Archives columns use original Booking ID, Client, event date/time, archive date and final status");
            check(newest.Bookings.Any(x=>x.Id==records[7].Id&&x.ArchivedBy=="Historical Archive Operator"),
                  "Hidden Archived By metadata remains available for internal audit tracing");
            var second=await service.GetTrashHistoryAsync(new(){Search=key,Page=2});
            check(second.Bookings.Count==3&&
                  second.Bookings.Select(x=>x.Id).SequenceEqual(records.AsEnumerable().Reverse().Skip(5).Select(x=>x.Id)) &&
                  second.Bookings.All(x=>!newest.Bookings.Any(y=>y.Id==x.Id)),
                  "Archive second page has all three remaining records without duplicates");
            check(second.Bookings.Single(x=>x.Id==records[0].Id).ArchiveDateRecorded==false,
                  "Legacy missing ArchivedAt stays Not recorded instead of acquiring a fabricated archive date");
            var oldest=await service.GetTrashHistoryAsync(new(){Search=key,SortBy="oldest"});
            check(oldest.Filters.SortBy=="oldest"&&oldest.Bookings.Select(x=>x.Id)
                    .SequenceEqual(records.Take(5).Select(x=>x.Id)),
                  "Oldest-first archives ordering uses ArchivedAt falling back to CreatedAt, not event date");
            var invalid=await service.GetTrashHistoryAsync(new(){Search=key,SortBy="HighestRevenue",Page=999,PageSize=99});
            check(invalid.Filters.SortBy=="newest"&&invalid.Filters.Page==2&&invalid.Filters.PageSize==5&&
                  invalid.Bookings.Count==3,
                  "Unsupported sorts normalize to newest and out-of-range pages clamp to five-record output");
            var matching=await service.GetTrashHistoryAsync(new(){Search=$"BK-{records[3].CreatedAt.Year}-{records[3].Id:D3}"});
            check(matching.TotalCount==1&&matching.Bookings.Single().Id==records[3].Id,
                  "Archive search finds real formatted booking identifiers");
            var date=records[4].ArchivedAt!.Value.Date;
            var dateFiltered=await service.GetTrashHistoryAsync(new(){Search=key,DateFrom=date,DateTo=date});
            check(dateFiltered.TotalCount==1&&dateFiltered.Bookings.Single().Id==records[4].Id,
                  "Archive date-range filtering uses stored ArchivedAt rather than event date");
            var detail=await service.GetDetailsAsync(records[2].Id);
            check(detail?.Trash.Id==records[2].Id&&detail.Trash.ArchiveDateRecorded&&
                  detail.Trash.BookingStatus==records[2].Status,
                  "Shared archive Details route remains read-only and preserves exact archived record");
            var count=await db.Bookings.AsNoTracking().CountAsync(b=>b.ClientId==client.Id);
            var packageStill=await db.Services.AsNoTracking().AnyAsync(x=>x.Id==package.Id&&x.IsArchived);
            var auditStill=await db.SystemActivities.AsNoTracking().AnyAsync(x=>x.Id==audit.Id&&
                  x.AffectedRecordId==records[7].Id.ToString());
            check(count==8&&packageStill&&auditStill&&
                  records.All(x=>x.ServiceId==package.Id&&x.Status is BookingStatus.Cancelled or BookingStatus.Declined or BookingStatus.Expired),
                  "Archive UI removal leaves historical package relationships, archived statuses and actor audit entries intact");
        } finally {
            db.SystemActivities.Remove(audit);
            db.Bookings.RemoveRange(records);
            db.Services.Remove(package);
            db.LegacyUsers.Remove(client);
            await db.SaveChangesAsync();
        }
    }
}
