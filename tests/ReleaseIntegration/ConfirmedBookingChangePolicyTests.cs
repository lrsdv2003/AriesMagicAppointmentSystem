using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;

internal static class ConfirmedBookingChangePolicyTests
{
    public static void Run()
    {
        int count=0;
        void Check(bool valid,string title){
            if(!valid)throw new Exception("FAILED: "+title);
            count++;Console.WriteLine("PASS: "+title);
        }
        var future=DateTime.Now.AddDays(21);
        var booking=new Booking {
            Id=17,Status=BookingStatus.Confirmed,StartTime=future,
            EventDate=future.Date,EndTime=future.AddHours(3),
            ServiceId=2,PackageName="Synthetic premium",PartyVenue="Reviewed old venue",
            VenueLatitude=14.58,VenueLongitude=121.12,
            ServiceZone="Zone A",TravelFeeStatus=VenueLocationPolicy.Accepted,
            BasePrice=12000m,FinalPrice=12000m,TravelFee=0m,
            RemovedInclusionsJson=null
        };
        booking.Payments.Add(new Payment {
            BookingId=17,Status=PaymentStatus.Verified,Amount=2000m
        });
        var now=DateTime.Now;
        Check(ConfirmedBookingChangePolicy.MayStart(booking,now),
            "Confirmed legacy verified bookings may propose edits without inventing payment timestamps");
        Check(!ConfirmedBookingChangePolicy.MayStart(booking,future),
            "Event already underway is not editable");
        booking.Payments.Add(new Payment{Status=PaymentStatus.Pending,Amount=1000m});
        Check(!ConfirmedBookingChangePolicy.MayStart(booking,now),
            "Payment under Owner verification prevents changing the confirmed contract");
        booking.Payments.Remove(booking.Payments.Last());
        booking.RescheduleRequests.Add(new RescheduleRequest{Status=RescheduleRequestStatus.Pending});
        Check(!ConfirmedBookingChangePolicy.MayStart(booking,now),
            "Pending reschedule blocks contract amendment");
        booking.RescheduleRequests.Clear();
        booking.RefundEligible=true;
        Check(!ConfirmedBookingChangePolicy.MayStart(booking,now),
            "Refund-eligible booking is not amended");
        booking.RefundEligible=false;
        booking.Status=BookingStatus.Cancelled;
        Check(!ConfirmedBookingChangePolicy.MayStart(booking,now),
            "Cancelled booking is never edited");
        booking.Status=BookingStatus.Confirmed;
        Check(!ConfirmedBookingChangePolicy.IsSafeTotal(1999.99m,2000m),
            "Cannot quote below existing verified payment");
        Check(ConfirmedBookingChangePolicy.IsSafeTotal(12000m,2000m),
            "Valid revised total preserves payment credit");
        var proposal=new ConfirmedBookingChangeProposal {
            BookingId=17,OriginalServiceId=booking.ServiceId,
            OriginalPackageName=booking.PackageName,OriginalVenue=booking.PartyVenue,
            OriginalBasePrice=booking.BasePrice,OriginalFinalPrice=booking.FinalPrice,
            OriginalTravelFee=booking.TravelFee,OriginalEndTime=booking.EndTime,
            OriginalEventDate=booking.EventDate,OriginalStartTime=booking.StartTime,
            OriginalLatitude=booking.VenueLatitude,OriginalLongitude=booking.VenueLongitude,
            OriginalServiceZone=booking.ServiceZone,OriginalTravelFeeStatus=booking.TravelFeeStatus,
            OriginalRemovedInclusionsJson=booking.RemovedInclusionsJson,
            OriginalDistanceKm=booking.DistanceKm,
            OriginalRequiresManualReview=booking.RequiresManualReview
        };
        Check(ConfirmedBookingChangePolicy.OriginalMatches(booking,proposal),
            "Unmodified original contract matches pending quote snapshot");
        booking.StartTime=booking.StartTime.AddMinutes(15);
        Check(!ConfirmedBookingChangePolicy.OriginalMatches(booking,proposal),
            "Schedule change invalidates pending Client approval");
        booking.StartTime=proposal.OriginalStartTime;
        booking.VenueLatitude=14.62;
        Check(!ConfirmedBookingChangePolicy.OriginalMatches(booking,proposal),
            "Changed venue coordinates invalidate pending Client approval");
        booking.VenueLatitude=proposal.OriginalLatitude;
        booking.FinalPrice++;
        Check(!ConfirmedBookingChangePolicy.OriginalMatches(booking,proposal),
            "Changed payment total invalidates pending Client approval");
        booking.FinalPrice=proposal.OriginalFinalPrice;
        booking.RequiresManualReview=true;
        Check(!ConfirmedBookingChangePolicy.OriginalMatches(booking,proposal),
            "Changed manual travel review state invalidates pending Client approval");
        booking.RequiresManualReview=false;
        booking.DistanceKm=42.5;
        Check(!ConfirmedBookingChangePolicy.OriginalMatches(booking,proposal),
            "Changed journey distance invalidates pending Client approval");
        Console.WriteLine("CONFIRMED_CHANGE_POLICY_CHECKS="+count);
    }
}
