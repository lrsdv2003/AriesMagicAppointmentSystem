using AriesMagicAppointmentSystem.Models;

namespace AriesMagicAppointmentSystem.Services;

public static class BookingCancellationPolicy
{
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
