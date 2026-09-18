using System.Reflection;
using System.Text;
using AriesMagicAppointmentSystem.Controllers;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

var checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAILED: " + name);
    checks++;
    Console.WriteLine("PASS: " + name);
}
foreach(var value in new[] { "unsuccessful", "unpaid", "not completed", "pending", "failed", "" })
    Check(!OcrComparison.IsSuccessful(value), "Reject positive OCR match for " + value);
Check(OcrComparison.IsSuccessful("Completed"), "Completed status matches");
Check(OcrComparison.ReceiverMatches("Aries Magic", "Aries Magic"), "Registered receiver matches");
Check(!OcrComparison.ReceiverMatches("Aries", "Aries Magic"), "Partial receiver does not match");
foreach(var name in new[] { "VerifyConfirmed", "RejectConfirmed", "RequestAdditionalEvidence", "ApproveRefund", "RejectRefund", "UploadRefundProof", "MarkAsRefunded" })
{
    var method = typeof(PaymentsController).GetMethod(name)!;
    Check(method.GetCustomAttributes<AuthorizeAttribute>().Any(a => a.Roles == "Owner"), name + " is Owner-only");
    Check(method.GetCustomAttributes<ValidateAntiForgeryTokenAttribute>().Any(), name + " requires anti-forgery");
}
var database = "AriesOwnerRegression_" + Guid.NewGuid().ToString("N");
var options = new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseSqlServer(@"Server=(localdb)\MSSQLLocalDB;Database=" + database + ";Trusted_Connection=True;TrustServerCertificate=True").Options;
await using var db = new ApplicationDbContext(options);
try
{
    await db.Database.EnsureCreatedAsync();
    var client = new User { FullName = "Regression Client", Email = "regression@example.test", Role = "Client" };
    var service = new Service { Name = "Regression Package", Price = 10000, DurationInHours = 2 };
    var active = new Booking { Client = client, Service = service, PackageName = service.Name, EventDate = new(2025,1,20), StartTime = new(2025,1,20,10,0,0), EndTime = new(2025,1,20,12,0,0), FinalPrice = 10000, Status = BookingStatus.Confirmed };
    var cancelled = new Booking { Client = client, Service = service, PackageName = service.Name, EventDate = new(2025,1,21), StartTime = new(2025,1,21,10,0,0), EndTime = new(2025,1,21,12,0,0), FinalPrice = 10000, Status = BookingStatus.Cancelled };
    var excluded = new Booking { Client = client, Service = service, PackageName = service.Name, EventDate = new(2025,2,20), StartTime = new(2025,2,20,10,0,0), EndTime = new(2025,2,20,12,0,0), FinalPrice = 10000, Status = BookingStatus.Confirmed };
    db.Bookings.AddRange(active, cancelled, excluded);
    await db.SaveChangesAsync();
    var receipt = new Payment { Booking = active, Amount = 3000, Status = PaymentStatus.Verified, VerifiedAt = new(2024,12,15), UploadedAt = new(2024,12,14) };
    var cancelledReceipt = new Payment { Booking = cancelled, Amount = 2000, Status = PaymentStatus.Verified, VerifiedAt = new(2025,1,15) };
    var excludedReceipt = new Payment { Booking = excluded, Amount = 9000, Status = PaymentStatus.Verified, VerifiedAt = new(2025,1,15) };
    var pending = new Payment { Booking = active, Amount = 500, Status = PaymentStatus.Pending };
    db.Payments.AddRange(receipt, cancelledReceipt, excludedReceipt, pending);
    await db.SaveChangesAsync();
    db.RefundRequests.AddRange(
        new RefundRequest { Booking = cancelled, OriginalPayment = cancelledReceipt, Amount = 2000, ApprovedAmount = 1000, Status = RefundStatus.Refunded },
        new RefundRequest { Booking = active, OriginalPayment = receipt, Amount = 700, Status = RefundStatus.Approved });
    await db.SaveChangesAsync();
    var financials = new PaymentFinancialService(db);
    var finance = await financials.GetSummaryAsync(active.Id);
    Check(finance.TotalVerifiedPayments == 3000 && finance.RemainingBalance == 7000, "Pending payment never reduces balance");
    Check(finance.CompletedRefunds == 0 && finance.NetCollected == 3000, "Approved refund never reduces collected revenue");
    var reports = new ReportsController(db, financials);
    var view = (ViewResult)await reports.Index(new(2025,1,1), new(2025,1,31), 1, 2025, service.Name, null);
    var report = (ReportDashboardViewModel)view.Model!;
    Check(report.TotalBookings == 2, "Report filters use event dates");
    Check(report.TotalCollectedRevenue == 5000, "Collections include selected cancelled booking and earlier receipt");
    Check(report.RefundsIssued == 1000 && report.NetCollectedRevenue == 4000, "Only completed refunds reduce net revenue");
    Check(report.OutstandingReceivables == 7000, "Cancelled booking is not an outstanding receivable");
    Check(report.MonthlyRevenue.Sum() == report.TotalCollectedRevenue, "Trend collections reconcile to financial summary");
    Check(report.PackageRevenue.Sum() == report.NetCollectedRevenue, "Package revenue reconciles to total");
    Check(report.MonthlyLabels.Contains("Dec 2024"), "Historical collection month remains visible");
    var export = (FileContentResult)await reports.ExportCsv(new(2025,1,1), new(2025,1,31), 1, 2025, service.Name, null);
    var csv = Encoding.UTF8.GetString(export.FileContents);
    Check(csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == 3, "CSV uses same booking cohort");
    Check(await reports.ExportCsv(new(2025,2,1), new(2025,1,1), null, null, null, null) is BadRequestObjectResult, "Invalid export range rejected");
    var payments = new PaymentsController(db, null!, null!, null!, null!, null!, null!, new ConfigurationBuilder().Build(), financials);
    var http = new DefaultHttpContext();
    payments.ControllerContext = new ControllerContext { HttpContext = http };
    payments.TempData = new TempDataDictionary(http, new EmptyTempData());
    Check(await payments.RejectConfirmed(pending.Id, " ") is RedirectToActionResult && pending.Status == PaymentStatus.Pending, "Payment rejection requires reason before mutation");
    Check(await payments.RejectRefund(1, "") is RedirectToActionResult, "Refund rejection requires reason");
    var oldOcr = new OcrVerification { Payment = pending, VerificationPurpose = OcrVerificationPurposes.PaymentProof, VerificationResult = OcrVerificationResults.MismatchDetected, ProcessedAt = new(2025,1,1) };
    var newOcr = new OcrVerification { Payment = pending, VerificationPurpose = OcrVerificationPurposes.PaymentProof, VerificationResult = OcrVerificationResults.LikelyMatch, ProcessedAt = new(2025,1,2) };
    db.OcrVerifications.AddRange(oldOcr, newOcr);
    await db.SaveChangesAsync();
    var queue = (ViewResult)await payments.PendingVerification("mismatch");
    Check(!((IEnumerable<Payment>)queue.Model!).Any(), "Old OCR mismatch is excluded after replacement");
    await InterfaceRegression.RunAsync(db, Check);
    await AdminRegression.RunAsync(db, Check);
    if (args.Contains("--render"))
    {
        await SnapshotRenderer.RenderAsync(options, report, active, pending, service,
            await db.RefundRequests.Include(r=>r.Booking).ThenInclude(b=>b!.Client).Include(r=>r.OriginalPayment).FirstAsync(),
            finance);
        Check(true, "Owner pages render with isolated fixture data");
    }
    Console.WriteLine("SUCCESS: " + checks + " Owner regression checks passed.");
}
finally
{
    await db.Database.EnsureDeletedAsync();
}
sealed class EmptyTempData : ITempDataProvider
{
    public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
    public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
}
