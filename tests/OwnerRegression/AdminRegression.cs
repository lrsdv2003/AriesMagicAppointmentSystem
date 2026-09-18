using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using AriesMagicAppointmentSystem.Controllers;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Extensions;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

static class AdminRegression
{
    static ClaimsPrincipal Principal(string role, string id = "fixture-admin") =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Role, role), new Claim(ClaimTypes.NameIdentifier, id)], "Fixture"));

    public static async Task RunAsync(ApplicationDbContext db, Action<bool,string> check)
    {
        check(!Principal("Admin").CanFollowModuleLink("/Payments/Verify/1"), "Historical Admin notifications cannot link to financial actions");
        check(Principal("Admin").CanFollowModuleLink("/Calendar/Index"), "System coordination links remain available");
        var services = new ServiceCollection().AddLogging().AddAuthorization().AddSingleton(db);
        services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
        await using var provider = services.BuildServiceProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var policies = provider.GetRequiredService<IAuthorizationPolicyProvider>();
        foreach(var name in new[] {"PendingVerification","Verify","VerifyConfirmed","RejectConfirmed","RequestAdditionalEvidence","RefundRequests","RefundReview","ApproveRefund","RejectRefund","UploadRefundProof","MarkAsRefunded"})
        {
            var attrs = typeof(PaymentsController).GetMethod(name)!.GetCustomAttributes<AuthorizeAttribute>();
            var policy = await AuthorizationPolicy.CombineAsync(policies, attrs);
            foreach(var role in new[]{"Admin","Staff","Client","Owner"})
                check((await authorization.AuthorizeAsync(Principal(role), null, policy!)).Succeeded == (role == "Owner"), role + " financial authorization: " + name);
        }
        foreach(var pair in new[] {(typeof(BookingsController),"Details"),(typeof(CalendarController),"GetReservationsByDate"),(typeof(RescheduleRequestsController),"Index")})
        {
            var policy = await AuthorizationPolicy.CombineAsync(policies, pair.Item1.GetMethod(pair.Item2)!.GetCustomAttributes<AuthorizeAttribute>());
            check(!(await authorization.AuthorizeAsync(Principal("Admin"), null, policy!)).Succeeded, "Admin denied operational " + pair.Item1.Name + "." + pair.Item2);
            check((await authorization.AuthorizeAsync(Principal("Staff"), null, policy!)).Succeeded, "Staff retains operational " + pair.Item1.Name + "." + pair.Item2);
        }
        check(typeof(CalendarController).GetMethod("UpdateMaxBookingsAjax") == null && typeof(CalendarController).GetMethod("SetDateLimitAjax") == null, "No mutable booking-limit endpoints");
        check(typeof(CommunicationsController).GetMethod("RequestOwnerFinancialReview") == null, "Legacy Admin financial review endpoint removed");

        var date = DateTime.Today.AddDays(90);
        var client = new User {FullName="Capacity Client", Email="", Role="Client"};
        var package = new Service {Name="Capacity Package", Price=10000, DurationInHours=1};
        Booking Make(int hour, string status) => new() {Client=client,Service=package,PackageName=package.Name,EventDate=date,StartTime=date.AddHours(hour),EndTime=date.AddHours(hour+1),FinalPrice=10000,Status=status};
        var first=Make(8,BookingStatus.Confirmed);var second=Make(10,BookingStatus.Confirmed);var third=Make(12,BookingStatus.AwaitingVerification);var fourth=Make(14,BookingStatus.AwaitingVerification);
        db.Bookings.AddRange(first,second,third,fourth);
        db.SystemSettings.Add(new SystemSetting {MaxBookingsPerDay=99});
        db.DateBookingLimits.Add(new DateBookingLimit {Date=date,MaxBookings=99});
        var thirdPayment=new Payment {Booking=third,Amount=2000,Status=PaymentStatus.Pending};
        var fourthPayment=new Payment {Booking=fourth,Amount=2000,Status=PaymentStatus.Pending};
        db.Payments.AddRange(thirdPayment,fourthPayment);await db.SaveChangesAsync();
        var http = new DefaultHttpContext {User=Principal("Owner")};
        var payments=new PaymentsController(db,null!,null!,null!,null!,null!,new SystemActivityService(db),new ConfigurationBuilder().Build(),new PaymentFinancialService(db)) {
            ControllerContext=new ControllerContext {HttpContext=http},TempData=new TempDataDictionary(http,new EmptyTempData())
        };
        await payments.VerifyConfirmed(thirdPayment.Id);
        check(third.Status==BookingStatus.Confirmed && thirdPayment.Status==PaymentStatus.Verified,"Third confirmed booking is accepted");
        await payments.VerifyConfirmed(fourthPayment.Id);
        check(fourth.Status==BookingStatus.AwaitingVerification && fourthPayment.Status==PaymentStatus.Pending,"Fourth confirmation rejected despite legacy limits of 99");
        var bookings=new BookingsController(db,null!,null!,null!,null!,null!,null!);
        var availability=(JsonResult)await bookings.CheckDateAvailability(date);
        var data=JsonSerializer.SerializeToElement(availability.Value);
        check(data.GetProperty("isFull").GetBoolean() && data.GetProperty("maxPerDay").GetInt32()==3,"Client availability uses fixed limit");
        var reschedules=new RescheduleRequestsController(db,null!,null!);
        var method=typeof(RescheduleRequestsController).GetMethod("CheckRequestedScheduleAvailabilityAsync",BindingFlags.NonPublic|BindingFlags.Instance)!;
        var blockedMove=await (Task<RescheduleAvailabilityViewModel>)method.Invoke(reschedules,[fourth.Id,date.AddHours(18),date.AddHours(19)])!;
        check(!blockedMove.IsAvailable && blockedMove.State=="Daily Limit Reached","Rescheduling cannot enter a full day");
        var sameDay=await (Task<RescheduleAvailabilityViewModel>)method.Invoke(reschedules,[third.Id,date.AddHours(18),date.AddHours(19)])!;
        check(sameDay.IsAvailable,"Rescheduling an existing booking excludes itself from capacity");
        http.User=Principal("Admin");
        check(await payments.PaymentProof(thirdPayment.Id) is ForbidResult,"Admin cannot fetch financial proof");
        var refund=await db.RefundRequests.FirstAsync();
        check(await payments.RefundProof(refund.Id) is ForbidResult,"Admin cannot fetch refund proof");
        check(await new TrashHistoryService(db).GetDetailsAsync(first.Id)==null,"Trash cannot disclose active booking by direct ID");

        var manager=provider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager=provider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach(var role in new[]{"Staff","Owner","Admin","Client"})
            if(!await roleManager.RoleExistsAsync(role)) await roleManager.CreateAsync(new IdentityRole(role));
        var staff=new ApplicationUser {UserName="admin-test-staff",Email="staff@example.test",FullName="Test Staff",PhoneNumber="123"};
        var owner=new ApplicationUser {UserName="admin-test-owner",Email="owner@example.test",FullName="Test Owner",PhoneNumber="123"};
        await manager.CreateAsync(staff);await manager.AddToRoleAsync(staff,"Staff");
        await manager.CreateAsync(owner);await manager.AddToRoleAsync(owner,"Owner");
        var users=new UserManagementController(manager,new SystemActivityService(db)) {ControllerContext=new ControllerContext {HttpContext=http},TempData=new TempDataDictionary(http,new EmptyTempData())};
        check(await users.DisableStaff(owner.Id) is ForbidResult && owner.IsActive,"Staff endpoint cannot disable Owner");
        check(await users.EditStaff(new StaffUserViewModel {Id=owner.Id,FullName="Tampered",Email=owner.Email!,PhoneNumber="123"}) is ForbidResult && owner.FullName=="Test Owner","Staff endpoint cannot edit Owner");
        await users.DisableStaff(staff.Id);
        check(!staff.IsActive,"Admin can deactivate permitted Staff account");
        await users.ActivateStaff(staff.Id);
        check(staff.IsActive,"Admin can reactivate permitted Staff account");
        var listing=(ViewResult)await users.Index(null, role:"Owner");
        check(((IEnumerable<ApplicationUser>)listing.Model!).Any(u=>u.Id==owner.Id),"Role filter includes Owner accounts");
        await new SystemActivityService(db).LogAsync(SystemActivityType.UserUpdated,"Fixture audit",owner.Id,owner.FullName);
        var log=await db.SystemActivities.OrderByDescending(a=>a.Id).FirstAsync();
        check(AdminActivityPresentation.ActorRole(log)=="Owner","Audit records capture role at time of action");
        check(AdminActivityPresentation.ActorRole(new SystemActivity())=="Not recorded","Historical roles are not fabricated");
    }
}
