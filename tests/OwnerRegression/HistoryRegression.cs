using System.Reflection;
using System.Security.Claims;
using System.Text;
using AriesMagicAppointmentSystem.Controllers;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

static class HistoryRegression
{
    public static async Task RunAsync(ApplicationDbContext db,Action<bool,string> check)
    {
        var services=new ServiceCollection().AddLogging().AddAuthorization().AddSingleton(db);
        services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
        await using var provider=services.BuildServiceProvider();
        var manager=provider.GetRequiredService<UserManager<ApplicationUser>>();
        var history=new HistoryService(db,manager,new PaymentFinancialService(db));
        var client=new User {FullName="History Fixture",Role="Client"};
        var package=new Service {Name="History Package",Price=10000};
        Booking Make(string status,int day)=>new(){Client=client,Service=package,PackageName=package.Name,Status=status,
            EventDate=DateTime.Today.AddDays(-day),StartTime=DateTime.Today.AddDays(-day).AddHours(10),EndTime=DateTime.Today.AddDays(-day).AddHours(12),FinalPrice=10000,PartyVenue="Fixture Venue"};
        var complete=Make(BookingStatus.Completed,10);var cancel=Make(BookingStatus.Cancelled,9);
        var decline=Make(BookingStatus.Declined,8);var expired=Make(BookingStatus.Expired,7);var pending=Make(BookingStatus.Pending,6);
        db.Bookings.AddRange(complete,cancel,decline,expired,pending);await db.SaveChangesAsync();
        db.Payments.Add(new Payment {Booking=complete,Amount=3000,Status=PaymentStatus.Verified});
        db.BookingTimelines.AddRange(new BookingTimeline {Booking=complete,EventType=TimelineEventType.BookingCompleted,CreatedAt=DateTime.Today.AddDays(-10)},
            new BookingTimeline {Booking=complete,EventType=TimelineEventType.PaymentVerified,Notes="Private financial fixture"});
        await db.SaveChangesAsync();
        var filters=new HistoryFilterViewModel {ServiceId=package.Id};
        var listing=await history.GetHistoryAsync(filters);
        check(listing.TotalCount==4 && listing.Bookings.All(b=>b.BookingStatus!=BookingStatus.Pending),"History includes only final booking statuses");
        check(listing.Bookings.Select(b=>b.BookingStatus).Distinct().Count()==4,"History renders actual final statuses");
        var code=$"BK-{complete.CreatedAt.Year}-{complete.Id:D3}";
        check((await history.GetHistoryAsync(new(){Search=code})).Bookings.Single().Id==complete.Id,"History finds displayed booking code");
        check((await history.GetHistoryAsync(new(){ServiceId=package.Id,BookingStatus=BookingStatus.Cancelled})).Bookings.Single().Id==cancel.Id,"History filters final status");
        check((await history.GetHistoryAsync(new(){ServiceId=package.Id,PaymentStatus=PaymentStatus.Verified})).Bookings.Single().Id==complete.Id,"History payment review filter matches row status");
        check(await history.GetDetailsAsync(pending.Id)==null,"Active booking excluded from historical detail route");
        check((await history.GetDetailsAsync(cancel.Id))?.Booking.Status==BookingStatus.Cancelled,"Cancelled detail remains readable");
        var detail=(await history.GetDetailsAsync(complete.Id))!;
        check(detail.AmountPaid==3000 && detail.RemainingBalance==7000 && detail.FinancialSummary!=null,"Owner history uses shared verified-payment summary");
        check(detail.CompletedAt==DateTime.Today.AddDays(-10),"History completion date does not become archive date");
        var pages=await history.GetHistoryAsync(new(){ServiceId=package.Id,Page=999,PageSize=2});
        check(pages.Filters.Page==2 && pages.Bookings.Count==2 && pages.TotalPages==2,"History clamps out-of-range pagination");
        var route=HistoryPresentation.Routes(new(){Search="Fixture",BookingStatus="Completed",DateRange="Year",Year=2026,ServiceId=package.Id,PaymentStatus="Verified",CompletedDateFrom=new(2026,1,1),PageSize=2},2);
        check(route["Search"]=="Fixture" && route["Page"]=="2" && route["BookingStatus"]=="Completed" && route["Year"]=="2026" && route["PaymentStatus"]=="Verified" && route["CompletedDateFrom"]=="2026-01-01","History links preserve all filters");
        check(!HistoryPresentation.ShowEvent(TimelineEventType.PaymentVerified,false) && !HistoryPresentation.ShowEvent(TimelineEventType.RefundRequested,false) && HistoryPresentation.ShowEvent(TimelineEventType.RescheduleApproved,false),"Staff timeline excludes financial events");
        check(HistoryPresentation.ShowEvent(TimelineEventType.PaymentVerified,true),"Owner timeline includes important financial events");
        ClaimsPrincipal Principal(string role)=>new(new ClaimsIdentity([new Claim(ClaimTypes.Role,role)],"Fixture"));
        var controller=new HistoryController(history){ControllerContext=new(){HttpContext=new DefaultHttpContext{User=Principal("Staff")}}};
        var staffFilters=new HistoryFilterViewModel {ServiceId=package.Id,PaymentStatus="Verified",RefundStatus="Refunded",SortBy="HighestRevenue"};
        var staff=(HistoryIndexViewModel)((ViewResult)await controller.Index(staffFilters)).Model!;
        check(staff.TotalCount==4 && staffFilters.PaymentStatus==null && staffFilters.RefundStatus==null && staffFilters.SortBy=="Newest","Staff cannot infer financial state using history filters");
        var authorization=provider.GetRequiredService<IAuthorizationService>();
        var policies=provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var policy=await AuthorizationPolicy.CombineAsync(policies,typeof(HistoryController).GetCustomAttributes<AuthorizeAttribute>());
        foreach(var role in new[]{"Staff","Owner","Admin","Client"})
            check((await authorization.AuthorizeAsync(Principal(role),null,policy!)).Succeeded==(role is "Staff" or "Owner"),role+" History access");
        check(await controller.ExportCsv(new()) is ForbidResult,"Staff cannot export financial history");
        controller.HttpContext.User=Principal("Owner");
        var export=(FileContentResult)await controller.ExportCsv(new(){Search=code});
        check(Encoding.UTF8.GetString(export.FileContents).Contains(",3000.00,7000.00,"),"History CSV exports amount paid, not booking price");
    }
}
