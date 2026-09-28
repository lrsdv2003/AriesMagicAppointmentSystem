using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using AriesMagicAppointmentSystem.Controllers;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;

static class ClientRegression
{
    public static async Task RunAsync(ApplicationDbContext db, Action<bool,string> check)
    {
        var services = new ServiceCollection().AddLogging().AddAuthorization().AddSingleton(db);
        services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
        await using var provider = services.BuildServiceProvider();
        var users = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var finance = new PaymentFinancialService(db);
        var history = new HistoryService(db,users,finance);
        var client = new User { FullName="Client UI fixture",Role="Client" };
        var shortPackage = new Service {Name="Client short package",Price=6000,DurationInHours=1};
        var longPackage = new Service {Name="Client long package",Price=12000,DurationInHours=4};
        var archived = new Service {Name="Client archived package",Price=8000,DurationInHours=2,IsArchived=true};
        var date=DateTime.Today.AddDays(30);
        var mine=new Booking {Client=client,Service=shortPackage,ApplicationUserId="client-ui-mine",PackageName=shortPackage.Name,Status=BookingStatus.AwaitingDownpayment,EventDate=date,StartTime=date.AddHours(9),EndTime=date.AddHours(10),FinalPrice=6000};
        var other=new Booking {Client=client,Service=shortPackage,ApplicationUserId="client-ui-other",PackageName=shortPackage.Name,Status=BookingStatus.Confirmed,EventDate=date,StartTime=date.AddHours(12),EndTime=date.AddHours(13),FinalPrice=6000};
        db.Services.AddRange(shortPackage,longPackage,archived);db.Bookings.AddRange(mine,other);await db.SaveChangesAsync();
        var http=new DefaultHttpContext {User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,"client-ui-mine"),new Claim(ClaimTypes.Role,"Client")},"Fixture"))};
        var controller=new BookingsController(db,users,history,null!,null!,finance,null!) {ControllerContext=new ControllerContext{HttpContext=http},TempData=new TempDataDictionary(http,new EmptyTempData())};
        check(await controller.Details(other.Id) is NotFoundResult,"Client cannot open another client's booking details");
        check(await controller.Details(mine.Id) is ViewResult {ViewName:"ClientDetails"},"Client opens their own dedicated booking details");
        var listing=(ViewResult)await controller.MyBookings();
        check(((IEnumerable<Booking>)listing.Model!).All(b=>b.ApplicationUserId=="client-ui-mine"),"Client list excludes other accounts");
        check(await controller.Dashboard() is ViewResult {ViewName:"ClientDashboard"},"Client dashboard uses the scoped booking list");
        var home=new HomeController(db);
        var catalog=(ViewResult)await home.Packages();
        check(!((IEnumerable<Service>)catalog.Model!).Any(s=>s.IsArchived),"Public catalog excludes archived packages");
        check(await home.Package(archived.Id) is NotFoundResult,"Archived package cannot be opened publicly by ID");
        JsonElement Json(IActionResult result)=>JsonSerializer.SerializeToElement(((JsonResult)result).Value);
        var availability=Json(await controller.CheckScheduleAvailability(date,new TimeSpan(10,0,0),null));
        var ids=availability.GetProperty("availableIds").EnumerateArray().Select(x=>x.GetInt32()).ToList();
        check(ids.Contains(shortPackage.Id)&&!ids.Contains(longPackage.Id),"Package availability respects each package's duration");
        check(!Json(await controller.CheckScheduleAvailability(DateTime.Today,new TimeSpan(10,0,0),shortPackage.Id)).GetProperty("available").GetBoolean(),"Availability enforces three-day advance notice");
        check(!Json(await controller.CheckScheduleAvailability(date,new TimeSpan(12,30,0),shortPackage.Id)).GetProperty("available").GetBoolean(),"Existing confirmed event blocks overlapping requests");
        db.BlockedDates.Add(new BlockedDate {Date=date.AddDays(1),Reason="Client fixture"});await db.SaveChangesAsync();
        check(!Json(await controller.CheckScheduleAvailability(date.AddDays(1),new TimeSpan(10,0,0),shortPackage.Id)).GetProperty("available").GetBoolean(),"Blocked dates reject availability requests");
        var errors=new List<ValidationResult>();
        var invalidBooking = new BookingStepTwoViewModel();
        check(!Validator.TryValidateObject(invalidBooking,new ValidationContext(invalidBooking),errors,true)&&errors.Count>=6,"Single-page booking still requires event, venue, contact, and guest data");
        check(ClientBookingPresentation.NextAction(mine,new(){HasAdditionalEvidenceRequired=true}).Contains("clearer"),"Additional payment evidence takes precedence in next steps");
        other.StartTime=mine.StartTime;other.EndTime=mine.EndTime;
        var lockPayment=new Payment {Booking=other,Amount=2000,Status=PaymentStatus.Pending};
        db.Payments.Add(lockPayment);await db.SaveChangesAsync();
        await controller.Details(mine.Id);
        check(controller.ViewData["IsScheduleUnavailable"] is true,"Client details flag a slot locked by another booking's payment");
        var lockedListing=(ViewResult)await controller.MyBookings();
        check(((List<int>)lockedListing.ViewData["UnavailableBookingIds"]!).Contains(mine.Id),"Booking list and details agree about unavailable schedules");
        lockPayment.Status=PaymentStatus.Rejected;await db.SaveChangesAsync();
        await controller.Details(mine.Id);
        check(controller.ViewData["IsScheduleUnavailable"] is false,"Rejected payment releases the unavailable schedule notice");
        // Confirm staff access still selects the internal view.
        http.User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.Role,"Staff")},"Fixture"));
        check(await controller.Details(other.Id) is ViewResult {ViewName:"StaffDetails"},"Staff details retain their internal view");
    }
}
