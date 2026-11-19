using System.Security.Claims;
using System.Text.Json;
using AriesMagicAppointmentSystem.Controllers;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.EntityFrameworkCore;

internal static class CalendarDateLimitRegression
{
    private static bool Successful(IActionResult result) =>
        result is JsonResult json && JsonSerializer.SerializeToElement(json.Value).GetProperty("success").GetBoolean();

    private static CalendarController Controller(ApplicationDbContext db)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "calendar-regression-owner"),
            new Claim(ClaimTypes.Name, "Synthetic Owner"),
            new Claim(ClaimTypes.Role, "Owner")
        ], "Fixture"));
        return new CalendarController(db, null!, new SystemActivityService(db))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } }
        };
    }

    public static async Task RunAsync(ApplicationDbContext db, Action<bool, string> check)
    {
        var action = typeof(CalendarController).GetMethod(nameof(CalendarController.SaveDateLimit))!;
        check(action.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()
            .Any(a => a.Roles == "Owner")
            && action.IsDefined(typeof(ValidateAntiForgeryTokenAttribute), true),
            "Date capacity mutation requires Owner role and anti-forgery");
        check(typeof(CalendarController).GetMethod(nameof(CalendarController.GetDateLimit))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()
            .Any(a => a.Roles == "Owner"), "Date limit lookup remains Owner-only");

        var dateA=DateTime.Today.AddDays(280);
        var dateB=dateA.AddDays(1);
        var controller=Controller(db);
        check(await DateBookingCapacity.EffectiveLimitAsync(db,dateA)==BookingRules.MaximumDailyBookings,
            "Dates without overrides keep the established default of three");

        check(Successful(await controller.SaveDateLimit(dateA,0,0,4)),
            "Owner creates a four-booking date-specific limit");
        var recordA=await db.DateBookingLimits.AsNoTracking().SingleAsync(x=>x.Date.Date==dateA);
        check(recordA.MaxBookings==4 && await DateBookingCapacity.EffectiveLimitAsync(db,dateA)==4,
            "New limit is persisted and returned by shared availability resolver");

        check(Successful(await controller.SaveDateLimit(dateB,0,0,2)),
            "Owner independently configures adjacent date");
        var recordB=await db.DateBookingLimits.AsNoTracking().SingleAsync(x=>x.Date.Date==dateB);
        check(await DateBookingCapacity.EffectiveLimitAsync(db,dateA)==4
           && await DateBookingCapacity.EffectiveLimitAsync(db,dateB)==2,
            "Neighbor date does not inherit or overwrite a date-specific change");

        check(Successful(await controller.SaveDateLimit(dateA,recordA.Id,4,5)),
            "Existing daily-limit record can be edited");
        check(await db.DateBookingLimits.AsNoTracking().CountAsync(x=>x.Date.Date==dateA)==1 &&
              await DateBookingCapacity.EffectiveLimitAsync(db,dateA)==5 &&
              await DateBookingCapacity.EffectiveLimitAsync(db,dateB)==2,
            "Edit updates the original record without duplicating or changing neighboring date");

        check(await controller.SaveDateLimit(dateA,recordA.Id,4,6) is ConflictObjectResult
              && await DateBookingCapacity.EffectiveLimitAsync(db,dateA)==5,
              "Stale Owner edit cannot overwrite a more recent saved limit");
        check(await controller.SaveDateLimit(dateA,recordB.Id,2,6) is ConflictObjectResult &&
              await DateBookingCapacity.EffectiveLimitAsync(db,dateB)==2,
              "Tampered record identifier cannot alter a different date");

        foreach(var invalid in new[]{-1,0,21,99})
            check(await controller.SaveDateLimit(dateA,recordA.Id,5,invalid) is BadRequestObjectResult
                && await DateBookingCapacity.EffectiveLimitAsync(db,dateA)==5,
                "Invalid limit " + invalid + " rejected without mutation");

        var legacy=await db.DateBookingLimits.AsNoTracking()
            .Where(x=>x.MaxBookings==99).OrderBy(x=>x.Id).FirstAsync();
        var occupied=await db.Bookings.AsNoTracking().CountAsync(x=>x.Status==BookingStatus.Confirmed
            && x.EventDate.Date==legacy.Date.Date);
        check(occupied==3 && await DateBookingCapacity.EffectiveLimitAsync(db,legacy.Date)==3,
            "Legacy invalid override 99 never bypasses three-confirmed-booking capacity");
        check(await controller.SaveDateLimit(legacy.Date,legacy.Id,99,2) is ConflictObjectResult,
            "Owner cannot lower a limit below confirmed occupancy");
        check(Successful(await controller.SaveDateLimit(legacy.Date,legacy.Id,99,4)),
            "Owner may repair a legacy invalid setting into a supported override");
        var client=new BookingsController(db,null!,null!,null!,null!,null!,null!);
        var dateAvailability=(JsonResult)await client.CheckDateAvailability(legacy.Date);
        var details=JsonSerializer.SerializeToElement(dateAvailability.Value);
        check(details.GetProperty("maxPerDay").GetInt32()==4 &&
              details.GetProperty("remainingSlots").GetInt32()==1 &&
              !details.GetProperty("isFull").GetBoolean(),
              "Client availability immediately reflects confirmed occupancy and saved override");
        var reschedules=new RescheduleRequestsController(db,null!,null!);
        var reschedule=(JsonResult)await reschedules.CheckRescheduleDateAvailability(legacy.Date);
        var rescheduleData=JsonSerializer.SerializeToElement(reschedule.Value);
        check(rescheduleData.GetProperty("maxPerDay").GetInt32()==4 &&
              rescheduleData.GetProperty("remainingSlots").GetInt32()==1,
              "Client reschedule availability uses the same effective limit");

        // The fourth payment had previously been rejected by the fixed three-slot guard.
        // The new date-specific capacity must permit the fourth confirmation, but no fifth.
        var fourth=await db.Bookings.AsNoTracking()
            .Where(x=>x.Status==BookingStatus.AwaitingVerification && x.EventDate.Date==legacy.Date.Date)
            .OrderByDescending(x=>x.Id).FirstAsync();
        var fourthProof=await db.Payments.AsNoTracking()
            .Where(x=>x.BookingId==fourth.Id && x.Status==PaymentStatus.Pending).FirstAsync();
        var paymentController=new PaymentsController(db,null!,null!,null!,null!,null!,
            new SystemActivityService(db),new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
            new PaymentFinancialService(db));
        paymentController.ControllerContext=new ControllerContext { HttpContext=controller.ControllerContext.HttpContext };
        paymentController.TempData=new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(
            controller.ControllerContext.HttpContext,new EmptyTempData());
        await paymentController.VerifyConfirmed(fourthProof.Id,2000m,null,null);
        check(await db.Bookings.AsNoTracking().AnyAsync(x=>x.Id==fourth.Id && x.Status==BookingStatus.Confirmed)
            && await db.Payments.AsNoTracking().AnyAsync(x=>x.Id==fourthProof.Id && x.Status==PaymentStatus.Verified),
            "Owner payment verification honors the increased fourth slot after date-limit edit");
        var capacityNow=(JsonResult)await client.CheckDateAvailability(legacy.Date);
        check(JsonSerializer.SerializeToElement(capacityNow.Value).GetProperty("remainingSlots").GetInt32()==0,
            "Fourth verified booking fills the new limit without creating an extra slot");

        var blockedDate=dateA.AddDays(4);
        db.BlockedDates.Add(new BlockedDate {Date=blockedDate,Reason="Fixture closure"});
        await db.SaveChangesAsync();
        check(Successful(await controller.SaveDateLimit(blockedDate,0,0,4)),
            "Saving a limit does not erase a separate blocked date");
        var blocked=(JsonResult)await client.CheckDateAvailability(blockedDate);
        check(JsonSerializer.SerializeToElement(blocked.Value).GetProperty("isBlocked").GetBoolean(),
            "Blocked-date closure keeps precedence over the stored capacity value");

        var duplicateDate=dateA.AddDays(7);
        db.DateBookingLimits.AddRange(
            new DateBookingLimit { Date=duplicateDate,MaxBookings=2 },
            new DateBookingLimit { Date=duplicateDate,MaxBookings=3 });
        await db.SaveChangesAsync();
        check(await controller.SaveDateLimit(duplicateDate,0,0,4) is ConflictObjectResult,
            "Legacy duplicate date-limit records are refused without destructive cleanup");

        var recorded=await db.SystemActivities.AsNoTracking()
            .CountAsync(x=>x.AffectedRecordType=="DateBookingLimit" &&
                x.Description.Contains("Daily booking limit"));
        check(recorded>=4, "Successful date-capacity changes retain Owner audit activity");
    }
}
