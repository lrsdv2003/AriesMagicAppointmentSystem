using System.Text.Json;
using System.Text.RegularExpressions;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using Microsoft.EntityFrameworkCore;

internal static class ProMaxRefundFixtureSeed
{
    public static async Task RunAsync()
    {
        var path=Path.Combine(Directory.GetCurrentDirectory(),".tmp-owner-build","unified-client-fixture.json");
        using var document=JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var root=document.RootElement;
        var database=root.GetProperty("Database").GetString()??"";
        if (!Regex.IsMatch(database, "^AriesBookingVisual_[0-9a-f]{32}$")
            || root.GetProperty("BaseUrl").GetString() != "http://127.0.0.1:5310")
            throw new InvalidOperationException("Synthetic fixture guard refused a non-local database.");

        var options=new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer($@"Server=(localdb)\MSSQLLocalDB;Database={database};Integrated Security=True;TrustServerCertificate=True").Options;
        await using var db=new ApplicationDbContext(options);
        var client=await db.Users.AsNoTracking().SingleAsync(u=>u.Email=="client@example.test");
        var archived=await db.Bookings.FirstAsync(b=>b.ApplicationUserId==client.Id && b.Status==BookingStatus.Declined);
        var cancelled=await db.Bookings.FirstAsync(b=>b.ApplicationUserId==client.Id && b.Status==BookingStatus.Cancelled);
        if (cancelled.RefundEligible || await db.RefundRequests.AnyAsync(r=>r.BookingId==cancelled.Id))
            throw new InvalidOperationException("Refusing to reseed an existing fixture refund.");

        var original = new Payment{
            BookingId=cancelled.Id,Status=PaymentStatus.Verified,Amount=2000m,
            ProofImagePath="synthetic-receipt.png",PaymentMethod="GCash",
            UploadedAt=DateTime.Now.AddDays(-2),VerifiedAt=DateTime.Now.AddDays(-1)};
        db.Payments.Add(original);
        await db.SaveChangesAsync();
        cancelled.RefundEligible=true;
        cancelled.RefundEligibilityReason="StaffApprovedCancellation";
        cancelled.RefundEligibilityPaymentId=original.Id;

        var historicPayment=new Payment{
            BookingId=archived.Id,Status=PaymentStatus.Verified,Amount=3000m,
            ProofImagePath="synthetic-receipt.png",PaymentMethod="GCash",
            UploadedAt=DateTime.Now.AddDays(-8),VerifiedAt=DateTime.Now.AddDays(-7)};
        db.Payments.Add(historicPayment);await db.SaveChangesAsync();
        db.RefundRequests.Add(new RefundRequest{
            BookingId=archived.Id,OriginalPaymentId=historicPayment.Id,
            Amount=historicPayment.Amount,PaymentProofImagePath="synthetic-receipt.png",
            EligibilityReason="StaffApprovedCancellation",GCashAccountName="Synthetic client",
            GCashNumber="09123456789",Status=RefundStatus.Rejected,
            AdminRemarks="Synthetic Owner record retained in booking history.",
            RequestedAt=DateTime.Now.AddDays(-6)
        });
        await db.SaveChangesAsync();
        Console.WriteLine($"PRO_MAX_REFUND_FIXTURE_READY eligible_booking={cancelled.Id} historical_booking={archived.Id} original_payment={original.Id}");
    }
}
