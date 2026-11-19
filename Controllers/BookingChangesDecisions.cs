using System.Data;
using System.Security.Claims;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AriesMagicAppointmentSystem.Controllers;

public sealed partial class BookingChangesController
{
    [HttpPost,Authorize(Roles="Client"),ValidateAntiForgeryToken]
    public async Task<IActionResult> Accept(int id,Guid token,bool acceptChange)
    {
        if(!acceptChange||token==Guid.Empty)
            return Deny(id,"Explicit acceptance of the revised event details is required.");
        await using var tx=await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var booking=await LoadAsync(id);
        if(booking==null||booking.ApplicationUserId!=User.FindFirstValue(ClaimTypes.NameIdentifier))
            return NotFound();
        var pending=await ConfirmedBookingChangePolicy.PendingAsync(_db,id);
        if(pending==null||pending.Token!=token)
            return Deny(id,"This change proposal is no longer pending.");
        if(!ConfirmedBookingChangePolicy.MayStart(booking,ManilaNow)
            || !ConfirmedBookingChangePolicy.OriginalMatches(booking,pending)
            || await HasRefundAsync(id)
            || Paid(booking)!=pending.VerifiedPaymentsAtProposal
            || PaidCount(booking)!=pending.VerifiedPaymentCountAtProposal
            || !ConfirmedBookingChangePolicy.IsSafeTotal(pending.NewFinalPrice,Paid(booking)))
            return Deny(id,"The booking or payments changed. Submit a fresh proposal after review.");
        if(pending.PackageChanged){
            var service=await _db.Services.AsNoTracking().FirstOrDefaultAsync(s=>
                s.Id==pending.NewServiceId&&!s.IsArchived);
            if(service==null||service.Price!=pending.NewBasePrice
                ||service.Name!=pending.NewPackageName
                ||booking.StartTime.AddHours(service.DurationInHours)!=pending.NewEndTime)
                return Deny(id,"The package or duration changed after the quote. A fresh proposal is needed.");
        }
        var route=await CheckRouteAsync(booking,pending.NewEndTime,
            pending.NewLatitude,pending.NewLongitude,pending.VenueChanged);
        if(route.Error!=null)return Deny(id,route.Error);
        if(pending.VenueChanged
           && (route.Quote==null||route.Quote.TravelFee!=pending.NewTravelFee
               ||route.Quote.ServiceZone!=pending.NewServiceZone
               ||route.Quote.RequiresManualReview!=pending.NewRequiresManualReview))
            return Deny(id,"Travel pricing or review requirements changed. Submit a new quote.");

        booking.ServiceId=pending.NewServiceId;
        booking.PackageName=pending.NewPackageName;
        booking.BasePrice=pending.NewBasePrice;
        booking.FinalPrice=pending.NewFinalPrice;
        booking.RemovedInclusionsJson=pending.NewRemovedInclusionsJson;
        booking.EndTime=pending.NewEndTime;
        booking.PartyVenue=pending.NewVenue;
        booking.VenueLatitude=pending.NewLatitude;
        booking.VenueLongitude=pending.NewLongitude;
        booking.DistanceKm=pending.NewDistanceKm;
        booking.TravelFee=pending.NewTravelFee;
        booking.ServiceZone=pending.NewServiceZone;
        booking.RequiresManualReview=pending.NewRequiresManualReview;
        if(pending.VenueChanged){
            booking.IsServiceable=true;
            booking.VenueSelectionMethod=VenueLocationPolicy.MapPin;
            booking.VenueVerificationStatus=VenueLocationPolicy.StaffVerified;
            booking.VenueStaffReviewedAtUtc=pending.ProposedAtUtc;
            booking.TravelFeeStatus=VenueLocationPolicy.Accepted;
            booking.TravelQuoteAcceptedAtUtc=DateTime.UtcNow;
            booking.VenueDirections=null;booking.VenueManualDetails=null;
        }
        _db.BookingTimelines.Add(new BookingTimeline{
            BookingId=id,EventType=ConfirmedBookingChangePolicy.Accepted,
            Notes=token.ToString("D"),CreatedAt=DateTime.UtcNow
        });
        await NotifyInternalAsync(booking,"Confirmed booking change accepted",
            $"Client accepted the change for BK-{booking.CreatedAt.Year}-{id:D3}. Revised total: PHP {booking.FinalPrice:N2}.");
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        TempData["Success"]="Your confirmed booking has been updated. Verified payments remain unchanged and your balance has been recalculated.";
        return RedirectToAction("Details","Bookings",new{id});
    }

    [HttpPost,Authorize(Roles="Client"),ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id,Guid token)
    {
        await using var tx=await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var booking=await LoadAsync(id);
        if(booking==null||booking.ApplicationUserId!=User.FindFirstValue(ClaimTypes.NameIdentifier))
            return NotFound();
        var pending=await ConfirmedBookingChangePolicy.PendingAsync(_db,id);
        if(pending==null||pending.Token!=token)return Deny(id,"Proposal is no longer pending.");
        _db.BookingTimelines.Add(new BookingTimeline{
            BookingId=id,EventType=ConfirmedBookingChangePolicy.Rejected,
            Notes=token.ToString("D"),CreatedAt=DateTime.UtcNow
        });
        await NotifyInternalAsync(booking,"Client declined confirmed booking change",
            $"The Client declined the proposed changes for BK-{booking.CreatedAt.Year}-{id:D3}. Original terms were preserved.");
        await _db.SaveChangesAsync();await tx.CommitAsync();
        TempData["Success"]="Proposal declined. Your existing confirmed booking and verified payments are unchanged.";
        return RedirectToAction("Details","Bookings",new{id});
    }

    [HttpPost,Authorize(Roles="Staff,Owner"),ValidateAntiForgeryToken]
    public async Task<IActionResult> Withdraw(int id,Guid token)
    {
        await using var tx=await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var booking=await LoadAsync(id);
        if(booking==null)return NotFound();
        var pending=await ConfirmedBookingChangePolicy.PendingAsync(_db,id);
        if(pending==null||pending.Token!=token)return Deny(id,"Proposal is no longer pending.");
        _db.BookingTimelines.Add(new BookingTimeline{
            BookingId=id,EventType=ConfirmedBookingChangePolicy.Withdrawn,
            Notes=token.ToString("D"),CreatedAt=DateTime.UtcNow
        });
        if(!string.IsNullOrWhiteSpace(booking.ApplicationUserId)){
            _db.Notifications.Add(new Notification{
                UserId=booking.ApplicationUserId,Title="Confirmed booking change withdrawn",
                Message=$"{(User.IsInRole("Owner") ? "Owner" : "Staff")} withdrew the proposed changes for BK-{booking.CreatedAt.Year}-{id:D3}. Your original booking is unchanged.",
                Link=$"/Bookings/Details/{id}",CreatedAt=DateTime.Now
            });
        }
        await _db.SaveChangesAsync();await tx.CommitAsync();
        TempData["Success"]="Proposal withdrawn. The original confirmed booking remains unchanged.";
        return RedirectToAction("Details","Bookings",new{id});
    }

    private async Task NotifyInternalAsync(Booking booking,string title,string message)
    {
        var staff=await _users.GetUsersInRoleAsync("Staff");
        var owners=await _users.GetUsersInRoleAsync("Owner");
        foreach(var user in staff.Concat(owners).Where(u=>u.IsActive).DistinctBy(u=>u.Id)){
            _db.Notifications.Add(new Notification{
                UserId=user.Id,Title=title,Message=message,
                Link=$"/Bookings/Details/{booking.Id}",CreatedAt=DateTime.Now
            });
        }
    }
}
