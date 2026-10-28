using AriesMagicAppointmentSystem.Models;
namespace AriesMagicAppointmentSystem.ViewModels;

// Presentation links only. Existing controllers remain the authority for every operation.
public sealed record ClientHubAction(string Title, string Description, string Label, string Href)
{
 public static bool CanPay(Booking b, BookingFinancialSummaryViewModel? f, bool unavailable) => !unavailable && f != null && f.RemainingBalance > 0 && !f.HasPendingVerification && !f.HasAdditionalEvidenceRequired && new[]{BookingStatus.AwaitingDownpayment,BookingStatus.Confirmed,BookingStatus.Completed}.Contains(b.Status);
 public static ClientHubAction For(Booking b, BookingFinancialSummaryViewModel? f, bool unavailable = false, bool pendingReschedule = false, bool canFeedback = false)
 {
  var details=$"/Bookings/Details/{b.Id}";
  if(unavailable)return new("Choose your next step", "This time is no longer available. Review your booking or message Staff before making a payment.","Review booking",details);
  if(new[]{BookingStatus.Cancelled,BookingStatus.Declined,BookingStatus.Expired}.Contains(b.Status))return new("Your request is closed",ClientBookingPresentation.NextAction(b,f),"View booking",details);
  if(f?.HasAdditionalEvidenceRequired==true)return new("A clearer receipt is needed",ClientBookingPresentation.NextAction(b,f),"Replace receipt","/Payments/MyUploads");
  if(pendingReschedule)return new("Your schedule change is under review","Your current schedule stays in place until Staff approval. You can review your request and its decision here.","View reschedule request","/RescheduleRequests/MyRequests");
  if(f?.HasPendingVerification==true||b.Status==BookingStatus.AwaitingVerification)return new("Your receipt is with the Owner","Uploading proof does not verify payment. Your balance updates after Owner verification.","View payment status","/Payments/MyUploads");
  if(CanPay(b,f,unavailable))return new("Your next payment step",b.Status==BookingStatus.AwaitingDownpayment?ClientBookingPresentation.NextAction(b,f):"A balance remains on this booking. Open the payment instructions when you are ready to send proof for Owner verification.","Upload payment proof",$"/Payments/Upload?bookingId={b.Id}");
  if(canFeedback)return new("How was your celebration?","Your completed event is eligible for feedback. Public sharing is always your choice.","Share your experience",$"/Feedback/Create?bookingId={b.Id}");
  return new(b.Status==BookingStatus.Pending?"Your request is with Staff":b.Status==BookingStatus.Confirmed?"Your event is confirmed":"Your booking, at a glance",ClientBookingPresentation.NextAction(b,f),b.Status==BookingStatus.Pending?"View request":"View event details",details);
 }
}
