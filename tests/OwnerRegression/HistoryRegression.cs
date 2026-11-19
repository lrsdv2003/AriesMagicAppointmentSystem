using System.Reflection;
using System.Security.Claims;
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
using Microsoft.EntityFrameworkCore;

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
        var future=Make(BookingStatus.Confirmed,-7);var rejectedProof=Make(BookingStatus.AwaitingDownpayment,5);
        db.Bookings.AddRange(complete,cancel,decline,expired,pending,future,rejectedProof);await db.SaveChangesAsync();
        db.Payments.Add(new Payment {Booking=complete,Amount=3000,Status=PaymentStatus.Verified});
        db.Payments.AddRange(new Payment {Booking=future,Amount=10000,Status=PaymentStatus.Verified},new Payment {Booking=rejectedProof,Amount=2000,Status=PaymentStatus.Rejected});
        db.BookingTimelines.AddRange(new BookingTimeline {Booking=complete,EventType=TimelineEventType.BookingCompleted,CreatedAt=DateTime.Today.AddDays(-10)},
            new BookingTimeline {Booking=complete,EventType=TimelineEventType.PaymentVerified,Notes="Private financial fixture"});
        await db.SaveChangesAsync();
        var filters=new HistoryFilterViewModel {ServiceId=package.Id};
        var listing=await history.GetHistoryAsync(filters);
        check(listing.TotalCount==1 && listing.Bookings.All(b=>b.BookingStatus==BookingStatus.Completed),"History includes only genuinely Completed booking states");
        var archives=new TrashHistoryService(db);
        var archived=await archives.GetTrashHistoryAsync(new(){Search="History Fixture"});
        check(archived.Bookings.Select(b=>b.Id).Order().SequenceEqual(new[]{cancel.Id,decline.Id,expired.Id}.Order()),"Archives contain Cancelled, Declined and Expired without hiding unstamped records");
        check(!listing.Bookings.Select(b=>b.Id).Intersect(archived.Bookings.Select(b=>b.Id)).Any(),"History and Archives do not overlap");
        check(archived.Bookings.All(b=>b.BookingStatus is BookingStatus.Cancelled or BookingStatus.Declined or BookingStatus.Expired),"Archive rows expose the recorded unsuccessful lifecycle state");
        var archivedCode=$"BK-{cancel.CreatedAt.Year}-{cancel.Id:D3}";
        check((await archives.GetTrashHistoryAsync(new(){Search=archivedCode})).Bookings.Single().Id==cancel.Id,"Archives can find the displayed full booking identifier");
        var archivePage=await archives.GetTrashHistoryAsync(new(){Search="History Fixture",Page=999,PageSize=2});
        check(archivePage.Filters.Page==1 && archivePage.Filters.PageSize==5 && archivePage.Bookings.Count==3,"Archive pagination clamps after record classification");
        check((await archives.GetDetailsAsync(cancel.Id))?.Trash.ReasonRecorded==false,"Archive missing reason is not presented as a fabricated invalid-booking reason");
        check(future.Status==BookingStatus.Confirmed && !listing.Bookings.Any(b=>b.Id==future.Id),"Verified future event is not completed by payment verification");
        check(rejectedProof.Status==BookingStatus.AwaitingDownpayment && await archives.GetDetailsAsync(rejectedProof.Id)==null,"Rejected proof does not archive an active booking");
        check(complete.Status==BookingStatus.Completed&&cancel.Status==BookingStatus.Cancelled&&decline.Status==BookingStatus.Declined&&expired.Status==BookingStatus.Expired&&pending.Status==BookingStatus.Pending,"Classification does not rewrite terminal or pending booking states");
        check((await history.GetSummaryAsync()).HistoryCount==await db.Bookings.CountAsync(b=>b.Status==BookingStatus.Completed),"History summary counts only Completed bookings");
        var code=$"BK-{complete.CreatedAt.Year}-{complete.Id:D3}";
        check((await history.GetHistoryAsync(new(){Search=code})).Bookings.Single().Id==complete.Id,"History finds displayed booking code");
        check((await history.GetHistoryAsync(new(){ServiceId=package.Id,BookingStatus=BookingStatus.Cancelled})).TotalCount==0,"Forged unsuccessful History filter cannot mix Archives into History");
        check((await history.GetHistoryAsync(new(){ServiceId=package.Id,PaymentStatus=PaymentStatus.Verified})).Bookings.Single().Id==complete.Id,"History payment review filter matches row status");
        check(await history.GetDetailsAsync(pending.Id)==null,"Active booking excluded from historical detail route");
        check(await history.GetDetailsAsync(cancel.Id)==null&&(await archives.GetDetailsAsync(cancel.Id))?.Trash.Id==cancel.Id,"Cancelled record remains readable through Archives, outside completed History");
        check(typeof(IHistoryService).GetMethod("GetHistoryForExportAsync")==null
            && typeof(HistoryService).GetMethod("GetHistoryForExportAsync")==null,
            "Exclusive unpaged History export query has been retired without changing completed-only classification");
        var detail=(await history.GetDetailsAsync(complete.Id))!;
        check(detail.AmountPaid==3000 && detail.RemainingBalance==7000 && detail.FinancialSummary!=null,"Owner history uses shared verified-payment summary");
        check(detail.CompletedAt==DateTime.Today.AddDays(-10),"History completion date does not become archive date");
        var pages=await history.GetHistoryAsync(new(){ServiceId=package.Id,Page=999,PageSize=2});
        check(pages.Filters.Page==1 && pages.Bookings.Count==1 && pages.TotalPages==1,"History clamps pagination after excluding unsuccessful records");
        check(!HistoryPresentation.Filtered(new(){BookingStatus=BookingStatus.Completed})
            && HistoryPresentation.Filtered(new(){Search="nonexistent",BookingStatus=BookingStatus.Completed}),
            "Default Completed-only History filter is not mistaken for a user-applied search on an empty dataset");        var route=HistoryPresentation.Routes(new(){Search="Fixture",BookingStatus="Completed",DateRange="Year",Year=2026,ServiceId=package.Id,PaymentStatus="Verified",CompletedDateFrom=new(2026,1,1),PageSize=2},2);
        check(route["Search"]=="Fixture" && route["Page"]=="2" && route["BookingStatus"]=="Completed" && route["Year"]=="2026" && route["PaymentStatus"]=="Verified" && route["CompletedDateFrom"]=="2026-01-01" && route["PageSize"]=="5","History links preserve all filters");
        check(!HistoryPresentation.ShowEvent(TimelineEventType.PaymentVerified,false) && !HistoryPresentation.ShowEvent(TimelineEventType.RefundRequested,false) && HistoryPresentation.ShowEvent(TimelineEventType.RescheduleApproved,false),"Staff timeline excludes financial events");
        check(HistoryPresentation.ShowEvent(TimelineEventType.PaymentVerified,true),"Owner timeline includes important financial events");
        ClaimsPrincipal Principal(string role)=>new(new ClaimsIdentity([new Claim(ClaimTypes.Role,role)],"Fixture"));
        var controller=new HistoryController(history,archives){ControllerContext=new(){HttpContext=new DefaultHttpContext{User=Principal("Staff")}}};
        var staffFilters=new HistoryFilterViewModel {ServiceId=package.Id,PaymentStatus="Verified",RefundStatus="Refunded",SortBy="HighestRevenue"};
        var staff=(HistoryIndexViewModel)((ViewResult)await controller.Index(staffFilters)).Model!;
        check(staff.TotalCount==1 && staffFilters.PaymentStatus==null && staffFilters.RefundStatus==null && staffFilters.SortBy=="Newest","Staff cannot infer financial state using history filters");
        check(await controller.Details(cancel.Id) is RedirectToActionResult {ControllerName:"Bookings",ActionName:"Details"},"Staff legacy historical link keeps authorized operational detail access");
        var authorization=provider.GetRequiredService<IAuthorizationService>();
        var policies=provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var policy=await AuthorizationPolicy.CombineAsync(policies,typeof(HistoryController).GetCustomAttributes<AuthorizeAttribute>());
        foreach(var role in new[]{"Staff","Owner","Admin","Client"})
            check((await authorization.AuthorizeAsync(Principal(role),null,policy!)).Succeeded==(role is "Staff" or "Owner"),role+" History access");
        check(new[]{"ExportCsv","ExportPdf","Print"}.All(action=>
            typeof(HistoryController).GetMethods().All(method=>method.Name!=action)),
            "History Print, CSV and PDF endpoints are absent for both Staff and Owner");
        controller.HttpContext.User=Principal("Owner");
        check(await controller.Details(cancel.Id) is RedirectToActionResult {ControllerName:"TrashHistory",ActionName:"Details"},"Owner legacy History link redirects to the unsuccessful archive record");
        check(await controller.Details(complete.Id) is ViewResult,
            "Retired History downloads do not affect completed booking Details");
    }
}
