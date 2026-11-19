using System.Reflection;
using AriesMagicAppointmentSystem.Controllers;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

internal static class OwnerBusinessActivityRegression
{
    public static async Task RunAsync(ApplicationDbContext db, Action<bool,string> check)
    {
        check(typeof(SystemActivityController).GetCustomAttributes<AuthorizeAttribute>()
            .Any(x=>x.Roles=="Owner"),"Business Activity controller remains Owner-only");
        check(typeof(SystemActivityController).GetMethods().All(m=>m.Name!="Details"),
            "Business Activity has no dedicated Details endpoint to retire");
        var audit=new SystemActivityService(db);
        var searchKey="activity-qa-"+Guid.NewGuid().ToString("N");
        var baseUtc=DateTime.UtcNow.Date.AddDays(-35);
        var rows=Enumerable.Range(0,8).Select(i=>new SystemActivity{
            Type=i%2==0?SystemActivityType.PaymentVerified:SystemActivityType.BookingApproved,
            Description="QA business event description "+i+" with all original audit details preserved.",
            PerformedByUserId="qa-actor",PerformedByUserName="QA Activity Actor "+i,
            AffectedRecordId=searchKey,AffectedRecordType="Booking",
            CreatedAt=baseUtc.AddDays(i),
            MetadataJson=System.Text.Json.JsonSerializer.Serialize(new {
                ActorRole="Owner",Trace="keep-target-metadata-"+i
            })
        }).ToList();
        var technical=new SystemActivity{
            Type=SystemActivityType.LoginFailed,Description="Technical event excluded from business log",
            PerformedByUserId="qa-actor",PerformedByUserName="QA Technical",
            AffectedRecordId=searchKey,AffectedRecordType="Account",
            CreatedAt=baseUtc.AddDays(9),MetadataJson="{\"ActorRole\":\"Owner\",\"Trace\":\"protected\"}"
        };
        db.SystemActivities.AddRange(rows);
        db.SystemActivities.Add(technical);
        await db.SaveChangesAsync();
        try
        {
            var (first,total)=await audit.GetPagedAsync(1,5,search:searchKey,businessOnly:true);
            check(total==8 && first.Count==5 &&
                first.Select(x=>x.Id).SequenceEqual(rows.OrderByDescending(x=>x.CreatedAt).Select(x=>x.Id).Take(5)),
                "Business Activity searches hidden Target metadata, filters technical events, and paginates newest first");
            var (second,secondTotal)=await audit.GetPagedAsync(2,5,search:searchKey,businessOnly:true);
            check(secondTotal==8 && second.Count==3 &&
                first.Select(x=>x.Id).Intersect(second.Select(x=>x.Id)).Count()==0 &&
                first.Concat(second).Select(x=>x.Id).Distinct().Count()==8,
                "Business Activity second page has accurate totals without skipped or repeated rows");
            var (oldest,oldCount)=await audit.GetPagedAsync(1,5,search:searchKey,businessOnly:true,sortBy:"oldest");
            check(oldCount==8 && oldest.Select(x=>x.Id).SequenceEqual(rows.Take(5).Select(x=>x.Id)),
                "Oldest-first sorting preserves chronological ordering and stable paging");
            var (byType,typeCount)=await audit.GetPagedAsync(1,5,type:SystemActivityType.PaymentVerified,
                search:searchKey,businessOnly:true);
            check(typeCount==4 && byType.All(x=>x.Type==SystemActivityType.PaymentVerified),
                "Business Activity type filter occurs before paging");
            var (range,dateCount)=await audit.GetPagedAsync(1,5,fromDate:baseUtc.AddDays(3),
                toDate:baseUtc.AddDays(4),search:searchKey,businessOnly:true);
            check(dateCount==2 && range.Select(x=>x.Id).Order().SequenceEqual(rows.Skip(3).Take(2).Select(x=>x.Id).Order()),
                "UTC date-range filtering retains recorded timestamps and respects inclusive end date");
            var controller=new SystemActivityController(audit);
            var paged=(SystemActivityIndexViewModel)((ViewResult)await controller.Index(
                search:searchKey,page:9999,pageSize:99,sortBy:"oldest")).Model!;
            check(paged.Page==2 && paged.PageSize==5 && paged.TotalCount==8 &&
                paged.TotalPages==2 && paged.Activities.Count==3 &&
                paged.Activities.Select(x=>x.Id).SequenceEqual(rows.Skip(5).Select(x=>x.Id)),
                "Owner controller forces five rows, clamps invalid page and retains selected sort");
            var noMatch=(SystemActivityIndexViewModel)((ViewResult)await controller.Index(
                search:"nonexistent-"+searchKey)).Model!;
            check(noMatch.Activities.Count==0 && noMatch.TotalCount==0 && noMatch.Page==1,
                "No-match activity search is an actual empty result, not an artificial filtered dataset");
            var persisted=await db.SystemActivities.AsNoTracking().Where(x=>x.AffectedRecordId==searchKey).ToListAsync();
            check(persisted.Count==9 && persisted.All(x=>x.MetadataJson!=null) &&
                persisted.Where(x=>x.Type!=SystemActivityType.LoginFailed).All(x=>
                    x.Description.StartsWith("QA business event description") &&
                    x.AffectedRecordType=="Booking" && x.CreatedAt>=baseUtc),
                "UI-only changes preserve all database audit targets, descriptions, timestamps and metadata");
        }
        finally
        {
            db.SystemActivities.RemoveRange(rows);
            db.SystemActivities.Remove(technical);
            await db.SaveChangesAsync();
        }
        check(!await db.SystemActivities.AsNoTracking().AnyAsync(x=>x.AffectedRecordId==searchKey),
            "Disposable Business Activity fixtures are removed without changing existing audit rows");
    }
}
