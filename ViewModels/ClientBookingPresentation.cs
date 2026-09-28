using AriesMagicAppointmentSystem.Models;
namespace AriesMagicAppointmentSystem.ViewModels;

public static class ClientBookingPresentation
{
    public static string Status(string status) => status switch
    {
        BookingStatus.Pending => "Awaiting staff review",
        BookingStatus.AwaitingDownpayment => "Downpayment needed",
        BookingStatus.AwaitingVerification => "Awaiting payment verification",
        _ => status
    };
    public static string NextAction(Booking booking, BookingFinancialSummaryViewModel? finance = null) => booking.Status switch
    {
        BookingStatus.Cancelled => "This booking has been cancelled.",
        BookingStatus.Declined => "This request was declined. You can message staff for details.",
        BookingStatus.Expired => "This booking has expired. Start a new request to check availability.",
        _ when finance?.HasAdditionalEvidenceRequired == true => "A clearer payment proof is needed. Open My Payments to replace it.",
        _ when finance?.HasPendingVerification == true => "Your payment proof is being reviewed. No action is needed right now.",
        BookingStatus.Pending => "Staff will review your request. Payment opens after approval.",
        BookingStatus.AwaitingDownpayment => "Upload your downpayment proof to continue.",
        BookingStatus.AwaitingVerification => "Your payment proof is awaiting review. No action is needed right now.",
        BookingStatus.Confirmed => "Your event is confirmed. Review your venue, time, and contact details before event day.",
        BookingStatus.Completed when finance?.RemainingBalance > 0 => "Your event is complete. You still have a balance to settle.",
        BookingStatus.Completed => "Your event is complete. Thank you for celebrating with Aries Magic.",
        _ => "Open this booking to see its latest details."
    };
}
