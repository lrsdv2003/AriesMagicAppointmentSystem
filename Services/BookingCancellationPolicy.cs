using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace AriesMagicAppointmentSystem.Services;

public static class BookingCancellationPolicy
{
    public const string OwnerCancellationSource = "OwnerCancellation";
    public const int MinimumReasonLength = 8;
    public const int MaximumReasonLength = 1000;

    public static bool IsOwnerCandidate(Booking booking, DateTime businessNow) =>
        booking.Status == BookingStatus.Confirmed
        && booking.StartTime > businessNow
        && booking.EndTime > booking.StartTime
        && !booking.IsCompletedLocked
        && !booking.CancelledAtUtc.HasValue
        && !booking.ArchivedAt.HasValue
        && !booking.RefundEligible
        && !booking.CancellationRequestMessageId.HasValue;

    public static async Task<bool> HasOwnerCancellationConflictAsync(ApplicationDbContext db, int bookingId) =>
        await db.Payments.AnyAsync(p => p.BookingId == bookingId
            && (p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.AdditionalEvidenceRequired))
        || await db.RefundRequests.AnyAsync(r => r.BookingId == bookingId
            && (r.Status == RefundStatus.Pending || r.Status == RefundStatus.UnderReview || r.Status == RefundStatus.Approved
                || r.Status == RefundStatus.RefundProcessing || r.Status == RefundStatus.RefundVerified))
        || await db.RescheduleRequests.AnyAsync(r => r.BookingId == bookingId && r.Status == RescheduleRequestStatus.Pending)
        || await ConfirmedBookingChangePolicy.PendingAsync(db, bookingId) != null;

    public static bool IsClientDirectCancellationAllowed(Booking booking, Payment? initialVerifiedPayment, DateTime nowUtc)
    {
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Cancellation decisions require a UTC timestamp.", nameof(nowUtc));
        return booking.Status is BookingStatus.Pending or BookingStatus.AwaitingDownpayment or BookingStatus.AwaitingVerification or BookingStatus.Confirmed
            && !booking.RefundEligible
            && initialVerifiedPayment?.Status == PaymentStatus.Verified
            && initialVerifiedPayment.InitialVerificationAtUtc.HasValue
            && nowUtc < initialVerifiedPayment.InitialVerificationAtUtc.Value.AddHours(24);
    }
}
