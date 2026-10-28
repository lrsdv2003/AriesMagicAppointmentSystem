using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.ViewModels;
static class ClientHubRegression
{
 public static void Run(Action<bool,string> check)
 {
  var booking=new Booking{Id=42,Status=BookingStatus.Pending};var finance=new BookingFinancialSummaryViewModel{BookingTotal=10000,RemainingBalance=10000};
  check(ClientHubAction.For(booking,finance).Href=="/Bookings/Details/42","Hub Pending links to actual request, not payment");
  booking.Status=BookingStatus.AwaitingDownpayment;check(ClientHubAction.For(booking,finance).Href=="/Payments/Upload?bookingId=42","Hub payable action preserves booking selection");
  check(!ClientHubAction.CanPay(booking,finance,true)&&ClientHubAction.For(booking,finance,true).Href=="/Bookings/Details/42","Hub unavailable schedule never suggests payment");
  finance.HasPendingVerification=true;check(!ClientHubAction.CanPay(booking,finance,false)&&ClientHubAction.For(booking,finance).Href=="/Payments/MyUploads","Hub pending receipt is not a verified payment");
  finance.HasPendingVerification=false;finance.HasAdditionalEvidenceRequired=true;check(!ClientHubAction.CanPay(booking,finance,false)&&ClientHubAction.For(booking,finance).Label=="Replace receipt","Hub additional evidence uses existing private replacement flow");
  finance.HasAdditionalEvidenceRequired=false;booking.Status=BookingStatus.AwaitingVerification;check(ClientHubAction.For(booking,finance).Href=="/Payments/MyUploads","Hub awaiting-verification status has honest next action");
  booking.Status=BookingStatus.Confirmed;check(ClientHubAction.For(booking,finance,false,true).Href=="/RescheduleRequests/MyRequests","Hub pending reschedule preserves currently effective booking");
  finance.RemainingBalance=0;check(!ClientHubAction.CanPay(booking,finance,false)&&ClientHubAction.For(booking,finance).Href=="/Bookings/Details/42","Hub paid upcoming event has details action");
  booking.Status=BookingStatus.Completed;check(ClientHubAction.For(booking,finance,false,false,true).Href=="/Feedback/Create?bookingId=42","Hub explicitly eligible feedback uses existing booking ID");
  check(ClientHubAction.For(booking,finance,false,false,false).Href=="/Bookings/Details/42","Hub cannot invent feedback eligibility");
  finance.RemainingBalance=123.45m;check(ClientHubAction.CanPay(booking,finance,false),"Hub completed-balance action follows existing eligibility");
  foreach(var state in new[]{BookingStatus.Pending,BookingStatus.Cancelled,BookingStatus.Declined,BookingStatus.Expired,BookingStatus.AwaitingVerification}){booking.Status=state;check(!ClientHubAction.CanPay(booking,finance,false),"Hub no payment action for "+state);}
  booking.Status=BookingStatus.Cancelled;check(ClientHubAction.For(booking,finance,false,false,true).Href=="/Bookings/Details/42","Hub closed request cannot become feedback or payment action");
 }
}
