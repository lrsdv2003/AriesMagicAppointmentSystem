using System.Security.Claims;
using AriesMagicAppointmentSystem.Controllers;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

static class SecurityRegression
{
    public static async Task RunAsync(ApplicationDbContext db, Action<bool, string> check)
    {
        byte[] png = ProductionHardeningRegression.Image();
        IFormFile Image(string name, string type, byte[] bytes) => new FormFile(new MemoryStream(bytes), 0, bytes.Length, "proof", name) { Headers = new HeaderDictionary(), ContentType = type };
        check(await ImageUploadValidation.HasMatchingSignatureAsync(Image("proof.png", "image/png", png)), "PNG signature agrees with extension and MIME type");
        check(!await ImageUploadValidation.HasMatchingSignatureAsync(Image("proof.jpg", "image/jpeg", png)), "PNG disguised as JPEG rejected");
        check(!await ImageUploadValidation.HasMatchingSignatureAsync(Image("proof.png", "image/webp", png)), "Mismatched MIME rejected");
        check(!await ImageUploadValidation.HasMatchingSignatureAsync(Image("proof.png", "image/png", png[..4])), "Truncated image signature rejected");

        var booking = await db.Bookings.FirstAsync(b => b.ApplicationUserId == null);
        var payment = new Payment { BookingId = booking.Id, Amount = 2000, Status = PaymentStatus.Verified };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();
        var first = new RefundRequest { BookingId = booking.Id, OriginalPayment = payment, Amount = 1500, Status = RefundStatus.UnderReview };
        var second = new RefundRequest { BookingId = booking.Id, OriginalPayment = payment, Amount = 1000, Status = RefundStatus.UnderReview };
        db.RefundRequests.AddRange(first, second);
        await db.SaveChangesAsync();
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "fixture-owner"), new Claim(ClaimTypes.Role, "Owner")], "Fixture")) };
        var controller = new PaymentsController(db, null!, null!, null!, null!, null!, new SystemActivityService(db), new ConfigurationBuilder().Build(), new PaymentFinancialService(db))
        {
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, new EmptyTempData())
        };
        await controller.ApproveRefund(first.Id);
        check(first.Status == RefundStatus.Approved && first.ApprovedAmount == 1500, "Refund approval reserves original payment funds");
        await controller.ApproveRefund(second.Id);
        check(second.Status == RefundStatus.UnderReview, "Cumulative refunds cannot exceed original receipt");
        var auditCount = await db.SystemActivities.CountAsync(a => a.AffectedRecordType == "RefundRequest" && a.AffectedRecordId == first.Id.ToString());
        await controller.ApproveRefund(first.Id);
        check(await db.SystemActivities.CountAsync(a => a.AffectedRecordType == "RefundRequest" && a.AffectedRecordId == first.Id.ToString()) == auditCount, "Repeated approval does not duplicate financial audit");
        first.Status = RefundStatus.RefundProcessing;
        first.RefundProofImagePath = "refund-confirmations/fixture.png";
        db.OcrVerifications.Add(new OcrVerification { RefundRequest = first, VerificationPurpose = OcrVerificationPurposes.RefundConfirmation });
        await db.SaveChangesAsync();
        await controller.MarkAsRefunded(first.Id);
        check(first.Status == RefundStatus.Refunded && first.RefundCompletedAt != null, "Refund completion records final state");
        var completed = first.RefundCompletedAt;
        await controller.MarkAsRefunded(first.Id);
        check(first.RefundCompletedAt == completed, "Duplicate completion preserves original completion time");
        await controller.ApproveRefund(second.Id);
        check(second.Status == RefundStatus.UnderReview, "Completed refunds continue to reserve original funds");

        var date = DateTime.Today.AddDays(180);
        var client = new User { FullName = "Concurrency fixture", Email = "", Role = "Client" };
        var package = await db.Services.FirstAsync();
        Booking Event(int hour, string status) => new() { Client = client, Service = package, EventDate = date,
            StartTime = date.AddHours(hour), EndTime = date.AddHours(hour + 1), FinalPrice = 10000, Status = status };
        var a = Event(8, BookingStatus.Confirmed);
        var b = Event(10, BookingStatus.Confirmed);
        var c = Event(12, BookingStatus.AwaitingVerification);
        var d = Event(14, BookingStatus.AwaitingVerification);
        var overlap = Event(8, BookingStatus.AwaitingVerification);
        db.Bookings.AddRange(a,b,c,d,overlap);
        var pc = new Payment { Booking = c, Amount = 2000, Status = PaymentStatus.Pending };
        var pd = new Payment { Booking = d, Amount = 2000, Status = PaymentStatus.Pending };
        var po = new Payment { Booking = overlap, Amount = 2000, Status = PaymentStatus.Pending };
        db.Payments.AddRange(pc,pd,po);
        await db.SaveChangesAsync();
        await controller.VerifyConfirmed(po.Id);
        check(po.Status == PaymentStatus.Pending && overlap.Status == BookingStatus.AwaitingVerification, "Verification rejects overlapping confirmed schedules below daily capacity");

        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(db.Database.GetConnectionString()).Options;
        async Task Compete(int id)
        {
            await using var connection = new ApplicationDbContext(options);
            var request = new DefaultHttpContext { User = http.User };
            var competing = new PaymentsController(connection, null!, null!, null!, null!, null!, new SystemActivityService(connection), new ConfigurationBuilder().Build(), new PaymentFinancialService(connection))
            { ControllerContext = new ControllerContext { HttpContext = request }, TempData = new TempDataDictionary(request, new EmptyTempData()) };
            try { await competing.VerifyConfirmed(id); }
            catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 1205) { /* SQL Server rolls back one contending transaction. */ }
            catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException sql && sql.Number == 1205) { }
            catch (InvalidOperationException ex) when (ex.InnerException is DbUpdateException update
                && update.InnerException is Microsoft.Data.SqlClient.SqlException sql && sql.Number == 1205) { /* EF can wrap its transient failure hint around the deadlock. */ }
        }
        await Task.WhenAll(Compete(pc.Id), Compete(pd.Id));
        check(await db.Bookings.AsNoTracking().CountAsync(x => x.EventDate == date && x.Status == BookingStatus.Confirmed) == 3, "Concurrent confirmations preserve fixed three-booking capacity");
        check(await db.Payments.AsNoTracking().CountAsync(x => (x.Id == pc.Id || x.Id == pd.Id) && x.Status == PaymentStatus.Verified) == 1, "Only one competing downpayment is applied");
    }
}
