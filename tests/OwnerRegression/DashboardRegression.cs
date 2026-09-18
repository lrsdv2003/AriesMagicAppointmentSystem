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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

static class DashboardRegression
{
    public static async Task<Dictionary<string,RoleDashboardViewModel>> RunAsync(ApplicationDbContext db, Action<bool,string> check)
    {
        var services = new ServiceCollection().AddLogging().AddAuthorization().AddSingleton(db);
        services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
        await using var provider = services.BuildServiceProvider();
        var manager=provider.GetRequiredService<UserManager<ApplicationUser>>();
        var staff=await db.Users.SingleAsync(u=>u.UserName=="admin-test-staff");
        var owner=await db.Users.SingleAsync(u=>u.UserName=="admin-test-owner");
        var client=new User {FullName="Dashboard Client",Email="",Role="Client"};
        var package=new Service {Name="Dashboard Magic Package with a Long Name",Price=15000,DurationInHours=1};
        Booking BookingAt(DateTime start,string status) => new() {Client=client,Service=package,PackageName=package.Name,EventDate=start.Date,StartTime=start,EndTime=start.AddHours(1),Status=status,FinalPrice=15000,PartyVenue="Antipolo Community Hall, Rizal",DistanceKm=12.4};
        var today1=BookingAt(DateTime.Today.AddHours(23),BookingStatus.Confirmed);
        var today2=BookingAt(DateTime.Today.AddHours(22),BookingStatus.Confirmed);
        var future=BookingAt(DateTime.Today.AddDays(3).AddHours(14),BookingStatus.Confirmed);
        var pending=BookingAt(DateTime.Today.AddDays(7).AddHours(14),BookingStatus.Pending);
        var approved=BookingAt(DateTime.Today.AddDays(8).AddHours(14),BookingStatus.AwaitingDownpayment);
        db.Bookings.AddRange(today1,today2,future,pending,approved);
        var conversation=new Conversation {CreatedByUserId=owner.Id,Participants=[new(){UserId=staff.Id},new(){UserId=owner.Id}],Messages=[new(){SenderId=owner.Id,MessageContent="Fixture scheduling update",SentAt=DateTime.UtcNow}]};
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();
        db.RescheduleRequests.Add(new RescheduleRequest {Booking=future,RequestedDate=future.EventDate.AddDays(1),RequestedStartTime=future.StartTime.AddDays(1),RequestedEndTime=future.EndTime.AddDays(1),Reason="Fixture"});
        db.SystemActivities.AddRange(
            new SystemActivity {Type=SystemActivityType.BookingApproved,PerformedByUserName="Test Staff",AffectedRecordType="Booking",AffectedRecordId=pending.Id.ToString()},
            new SystemActivity {Type=SystemActivityType.UserUpdated,PerformedByUserName="Test Admin",AffectedRecordType="ApplicationUser",AffectedRecordId=staff.Id},
            new SystemActivity {Type=SystemActivityType.PaymentVerified,PerformedByUserName="Test Owner",AffectedRecordType="Payment",AffectedRecordId="1"});
        await db.SaveChangesAsync();
        var finance=new PaymentFinancialService(db);
        var history=new HistoryService(db,manager,finance);
        DashboardController Controller(string role,string uid,IPaymentFinancialService? financial=null) => new(db,financial??finance,history,NullLogger<DashboardController>.Instance) {
            ControllerContext=new ControllerContext {HttpContext=new DefaultHttpContext {User=new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role,role),new Claim(ClaimTypes.NameIdentifier,uid)],"Fixture"))}}
        };
        var staffModel=(RoleDashboardViewModel)((ViewResult)await Controller("Staff",staff.Id).Staff()).Model!;
        check(staffModel.UnavailableSections.Count==0,"Staff dashboard sections load successfully");
        check(staffModel.PendingBookings==await db.Bookings.CountAsync(b=>b.Status==BookingStatus.Pending),"Staff count matches Pending booking module");
        check(staffModel.RecentBookingRequests.All(b=>b.Status==BookingStatus.Pending) && !staffModel.RecentBookingRequests.Any(b=>b.Id==approved.Id),"Staff preview excludes already approved requests");
        check(staffModel.TodayEvents.Select(b=>b.Id).SequenceEqual(new[]{today2.Id,today1.Id}),"Today's events are chronological");
        check(staffModel.UpcomingEvents.All(b=>b.EventDate>DateTime.Today),"Upcoming events exclude today");
        check(staffModel.UnreadConversations==1,"Unread indicator counts member conversations with unseen messages");
        check(staffModel.VerifiedRevenue==0 && staffModel.RecentPaymentsToVerify.Count==0,"Staff model contains no financial data");
        check(staffModel.RecentActivity.All(a=>a.Type!=SystemActivityType.PaymentVerified && a.Type!=SystemActivityType.UserUpdated),"Staff activity excludes finance and administration");
        pending.Status=BookingStatus.AwaitingDownpayment;await db.SaveChangesAsync();
        var refreshed=(RoleDashboardViewModel)((ViewResult)await Controller("Staff",staff.Id).Staff()).Model!;
        check(refreshed.PendingBookings==staffModel.PendingBookings-1,"Dashboard counts refresh after booking status changes");

        var ownerModel=(RoleDashboardViewModel)((ViewResult)await Controller("Owner",owner.Id).Owner()).Model!;
        check(ownerModel.UnavailableSections.Count==0,"Owner dashboard sections load successfully");
        var report=(ReportDashboardViewModel)((ViewResult)await new ReportsController(db,finance).Index(null,null,null,null,null,null)).Model!;
        check(ownerModel.VerifiedRevenue==report.TotalCollectedRevenue && ownerModel.CompletedRefunds==report.RefundsIssued && ownerModel.NetCollectedRevenue==report.NetCollectedRevenue,"Owner collected/refund/net totals match unfiltered Reports including cancelled bookings");
        check(ownerModel.OutstandingReceivables==report.OutstandingReceivables && ownerModel.BookingValue==report.TotalBookingValue,"Owner receivables and booking value match Reports");
        check(ownerModel.PendingPayments==await db.Payments.CountAsync(p=>p.Status==PaymentStatus.Pending),"Payment priority matches verification queue");
        check(ownerModel.RecentPaymentsToVerify.Count<=4 && ownerModel.RecentRefundRequests.Count<=4,"Financial queue previews stay compact");
        check(ownerModel.BookingTrend.Count==6,"Business trend includes six event months");
        var failed=(RoleDashboardViewModel)((ViewResult)await Controller("Owner",owner.Id,new FailingDashboardFinance(finance)).Owner()).Model!;
        check(failed.UnavailableSections.SetEquals(new[]{"financial overview"}) && failed.QuickActions.Count>0 && failed.PendingPayments==ownerModel.PendingPayments,"Financial widget failure preserves queues and navigation without fake zero totals");

        var adminModel=(RoleDashboardViewModel)((ViewResult)await Controller("Admin","fixture-admin").Admin()).Model!;
        check(adminModel.UnavailableSections.Count==0,"Admin dashboard sections load successfully");
        var userController=new UserManagementController(manager,new SystemActivityService(db));
        var activeUsers=(IEnumerable<ApplicationUser>)((ViewResult)await userController.Index(null,"Active")).Model!;
        check(adminModel.TotalUsers==await db.Users.CountAsync() && adminModel.ActiveUsers==activeUsers.Count(),"Admin account totals match User Management");
        check(adminModel.TrashedBookingsCount==await db.Bookings.CountAsync(b=>b.Status==BookingStatus.Declined||b.Status==BookingStatus.Cancelled||b.Status==BookingStatus.Expired),"Admin archive count matches Trash History");
        check(adminModel.RecentPaymentsToVerify.Count==0 && adminModel.UpcomingEvents.Count==0 && adminModel.VerifiedRevenue==0,"Admin model excludes operational and financial data");
        check(adminModel.RecentActivity.All(a=>a.Type!=SystemActivityType.PaymentVerified && a.Type!=SystemActivityType.BookingApproved),"Admin activity preview stays system-focused");
        check(adminModel.QuickActions.All(a=>!a.Href.StartsWith("/Payments")&&!a.Href.StartsWith("/Reports")&&!a.Href.StartsWith("/Bookings")),"Admin shortcuts respect role boundaries");

        var authorization=provider.GetRequiredService<IAuthorizationService>();
        var policies=provider.GetRequiredService<IAuthorizationPolicyProvider>();
        foreach(var action in new[]{"Staff","Owner","Admin"})
        {
            var policy=await AuthorizationPolicy.CombineAsync(policies,typeof(DashboardController).GetMethod(action)!.GetCustomAttributes<AuthorizeAttribute>());
            foreach(var role in new[]{"Staff","Owner","Admin","Client"})
            {
                var user=new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role,role)],"Fixture"));
                check((await authorization.AuthorizeAsync(user,null,policy!)).Succeeded==(role==action),role+" dashboard authorization for "+action);
            }
        }
        return new() {["Staff"]=staffModel,["Owner"]=ownerModel,["Admin"]=adminModel,["OwnerFailure"]=failed};
    }
}
sealed class FailingDashboardFinance(IPaymentFinancialService inner) : IPaymentFinancialService
{
    public Task<Dictionary<int,BookingFinancialSummaryViewModel>> GetSummariesAsync(IEnumerable<int> ids) => throw new InvalidOperationException("Fixture simulated service failure");
    public Task<BookingFinancialSummaryViewModel> GetSummaryAsync(int id) => inner.GetSummaryAsync(id);
    public BookingFinancialSummaryViewModel Calculate(Booking booking,IEnumerable<Payment> payments,IEnumerable<RefundRequest> refunds) => inner.Calculate(booking,payments,refunds);
}
