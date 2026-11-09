using System.Data;
using System.Security.Claims;
using System.Text.Json;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AriesMagicAppointmentSystem.Controllers;

/// <summary>Confirmed booking changes require Staff proposal and Client acceptance.
/// Append-only timeline entries store pending revisions and resolutions without schema changes.</summary>
[Authorize]
public sealed partial class BookingChangesController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IVenueDistanceService _distance;
    private readonly IGeocodingService _geocoder;
    private readonly UserManager<ApplicationUser> _users;
    public BookingChangesController(ApplicationDbContext db, IVenueDistanceService distance,
        IGeocodingService geocoder, UserManager<ApplicationUser> users)
    { _db=db; _distance=distance; _geocoder=geocoder; _users=users; }

    private static DateTime ManilaNow => TimeZoneInfo.ConvertTimeFromUtc(
        DateTime.UtcNow, FeedbackPolicy.BusinessZone);

    private Task<Booking?> LoadAsync(int id) => _db.Bookings
        .Include(b=>b.Payments).Include(b=>b.RescheduleRequests).Include(b=>b.Client)
        .AsSplitQuery().FirstOrDefaultAsync(b=>b.Id==id);
    private Task<bool> HasRefundAsync(int id) => _db.RefundRequests.AnyAsync(r=>r.BookingId==id);
    private static decimal Paid(Booking b) =>
        b.Payments.Where(p=>p.Status==PaymentStatus.Verified).Sum(p=>p.Amount);
    private static int PaidCount(Booking b) =>
        b.Payments.Count(p=>p.Status==PaymentStatus.Verified);
    private IActionResult Deny(int id,string message) {
        TempData["Error"]=message;
        return RedirectToAction("Details","Bookings",new{id});
    }

    [HttpGet,Authorize(Roles="Staff")]
    public async Task<IActionResult> Edit(int id)
    {
        var booking=await LoadAsync(id);
        if(booking==null)return NotFound();
        if(string.IsNullOrWhiteSpace(booking.ApplicationUserId)
            || !ConfirmedBookingChangePolicy.MayStart(booking,ManilaNow)
            || await HasRefundAsync(id))
            return Deny(id,"This event must be upcoming and confirmed, with verified payment and no active payment, refund or reschedule review.");
        if(await ConfirmedBookingChangePolicy.PendingAsync(_db,id)!=null)
            return Deny(id,"The Client already has a pending proposal. Withdraw it before submitting another.");
        var services=await _db.Services.AsNoTracking()
            .Where(s=>!s.IsArchived||s.Id==booking.ServiceId)
            .OrderBy(s=>s.Price).ThenBy(s=>s.Name).ToListAsync();
        return View(new ConfirmedBookingChangeEditViewModel {
            Booking=booking, AvailablePackages=services, VerifiedPayments=Paid(booking),
            Input=new ConfirmedBookingChangeInput {
                BookingId=id,ServiceId=booking.ServiceId,Venue=booking.PartyVenue,
                Latitude=booking.VenueLatitude,Longitude=booking.VenueLongitude
            }
        });
    }

    [HttpGet,Authorize(Roles="Staff")]
    public async Task<IActionResult> VenueSuggestions(string? q,CancellationToken cancellationToken)
    {
        var search=q?.Trim();
        if(search==null||search.Length<5||search.Length>180)return Json(Array.Empty<object>());
        var matches=await _geocoder.SearchAsync(search,cancellationToken);
        return Json(matches.Where(m=>m.IsPrecise
            && VenueLocationPolicy.HasCoordinatePair(m.Latitude,m.Longitude)
            && VenueLocationPolicy.InPhilippines(m.Latitude,m.Longitude))
            .Take(6).Select(m=>new {name=m.DisplayName,latitude=m.Latitude,longitude=m.Longitude}));
    }

    [HttpPost,Authorize(Roles="Staff"),ValidateAntiForgeryToken]
    public async Task<IActionResult> Propose(ConfirmedBookingChangeInput input)
    {
        var id=input.BookingId;
        if(!ModelState.IsValid)return Deny(id,"Provide a valid package, full address and reason of at least 10 characters.");
        var venue=input.Venue.Trim();
        await using var tx=await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var booking=await LoadAsync(id);
        if(booking==null)return NotFound();
        if(string.IsNullOrWhiteSpace(booking.ApplicationUserId)
           || !ConfirmedBookingChangePolicy.MayStart(booking,ManilaNow) || await HasRefundAsync(id))
            return Deny(id,"This booking is not eligible for changes during payment, refund or rescheduling review.");
        if(await ConfirmedBookingChangePolicy.PendingAsync(_db,id)!=null)
            return Deny(id,"A change is already awaiting Client approval.");
        bool packageChanged=input.ServiceId!=booking.ServiceId;
        bool venueChanged=!string.Equals(venue,booking.PartyVenue.Trim(),StringComparison.OrdinalIgnoreCase);
        if(!packageChanged&&!venueChanged)return Deny(id,"Select a different package or venue to propose a change.");
        var service=await _db.Services.AsNoTracking().FirstOrDefaultAsync(s=>
            s.Id==input.ServiceId&&(!s.IsArchived||s.Id==booking.ServiceId));
        if(service==null)return Deny(id,"The selected package is unavailable.");

        // Preserve the current contract for venue-only edits. Switching packages
        // resets removed inclusions and uses the current catalog price.
        decimal newBase=packageChanged?service.Price:booking.BasePrice;
        string? newRemoved=packageChanged?null:booking.RemovedInclusionsJson;
        decimal deduction=0m;
        if(!packageChanged&&!string.IsNullOrWhiteSpace(newRemoved)){
            try {
                if((JsonSerializer.Deserialize<List<string>>(newRemoved)??new()).Count>0)
                    deduction=2000m; // existing fixed one-time removal rule
            }catch(JsonException){return Deny(id,"Existing inclusion adjustments must be reviewed before editing.");}
        }
        var newEnd=packageChanged?booking.StartTime.AddHours(service.DurationInHours):booking.EndTime;
        if(newEnd<=booking.StartTime||newEnd>booking.StartTime.AddDays(1))
            return Deny(id,"The package duration is invalid for this schedule.");

        double? lat=booking.VenueLatitude,lng=booking.VenueLongitude;
        decimal fee=booking.TravelFee;
        double? km=booking.DistanceKm;
        var zone=booking.ServiceZone;
        bool manualReview=booking.RequiresManualReview;
        if(venueChanged){
            if(!VenueLocationPolicy.HasCoordinatePair(input.Latitude,input.Longitude)
                || !VenueLocationPolicy.InPhilippines(input.Latitude!.Value,input.Longitude!.Value))
                return Deny(id,"Select a precise Philippine venue location pin or enter verified coordinates.");
            lat=input.Latitude;lng=input.Longitude;
            if(lat==booking.VenueLatitude && lng==booking.VenueLongitude)
                return Deny(id,"The changed venue requires a new reviewed location pin.");
        }
        var route=await CheckRouteAsync(booking,newEnd,lat,lng,venueChanged);
        if(route.Error!=null)return Deny(id,route.Error);
        if(venueChanged){
            fee=route.Quote!.TravelFee;km=route.Quote.DistanceKm;
            zone=route.Quote.ServiceZone;manualReview=route.Quote.RequiresManualReview;
        }
        decimal total=venueChanged&&!packageChanged
            ? Math.Max(0m,booking.FinalPrice-booking.TravelFee+fee)
            : Math.Max(0m,newBase-deduction+fee);
        decimal verified=Paid(booking);
        if(!ConfirmedBookingChangePolicy.IsSafeTotal(total,verified))
            return Deny(id,"New total cannot be below verified payments. Owner must reconcile any potential credit or refund.");
        var proposal=new ConfirmedBookingChangeProposal {
            Token=Guid.NewGuid(),BookingId=id,ProposedAtUtc=DateTime.UtcNow,
            ProposedByUserId=User.FindFirstValue(ClaimTypes.NameIdentifier)??"",
            Reason=input.Reason.Trim(),
            OriginalServiceId=booking.ServiceId,OriginalPackageName=booking.PackageName,
            OriginalVenue=booking.PartyVenue,OriginalBasePrice=booking.BasePrice,
            OriginalFinalPrice=booking.FinalPrice,OriginalTravelFee=booking.TravelFee,
            OriginalEndTime=booking.EndTime,
            OriginalEventDate=booking.EventDate,OriginalStartTime=booking.StartTime,
            OriginalLatitude=booking.VenueLatitude,OriginalLongitude=booking.VenueLongitude,
            OriginalServiceZone=booking.ServiceZone,OriginalTravelFeeStatus=booking.TravelFeeStatus,
            OriginalRemovedInclusionsJson=booking.RemovedInclusionsJson,
            OriginalDistanceKm=booking.DistanceKm,
            OriginalRequiresManualReview=booking.RequiresManualReview,
            VerifiedPaymentsAtProposal=verified,VerifiedPaymentCountAtProposal=PaidCount(booking),
            NewServiceId=service.Id,NewPackageName=packageChanged?service.Name:booking.PackageName,
            NewBasePrice=newBase,NewFinalPrice=total,NewTravelFee=fee,
            NewVenue=venueChanged?venue:booking.PartyVenue,
            NewLatitude=lat,NewLongitude=lng,NewDistanceKm=km,
            NewServiceZone=zone,NewRequiresManualReview=manualReview,
            NewEndTime=newEnd,NewRemovedInclusionsJson=newRemoved
        };
        _db.BookingTimelines.Add(new BookingTimeline {
            BookingId=id,EventType=ConfirmedBookingChangePolicy.Proposed,
            Notes=JsonSerializer.Serialize(proposal),CreatedAt=DateTime.UtcNow
        });
        _db.Notifications.Add(new Notification {
            UserId=booking.ApplicationUserId!,Title="Review your confirmed booking change",
            Message=$"Staff proposed updated event terms for BK-{booking.CreatedAt.Year}-{id:D3}. Your booking remains unchanged until you approve.",
            Link=$"/Bookings/Details/{id}",CreatedAt=DateTime.Now
        });
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        TempData["Success"]="Proposal sent to Client. Verified payments and confirmed booking remain unchanged until acceptance.";
        return RedirectToAction("Details","Bookings",new{id});
    }

    private async Task<(string? Error,VenueDistanceResult? Quote)> CheckRouteAsync(
        Booking current,DateTime end,double? latitude,double? longitude,bool venueChanged)
    {
        var peers=await _db.Bookings.AsNoTracking()
            .Where(b=>b.Id!=current.Id&&b.Status==BookingStatus.Confirmed
                &&b.EventDate.Date==current.EventDate.Date)
            .OrderBy(b=>b.StartTime).ToListAsync();
        if(peers.Any(b=>current.StartTime<b.EndTime.AddHours(1)&&end>b.StartTime))
            return ("New package duration conflicts with another confirmed booking.",null);
        if(!venueChanged&&end==current.EndTime)return(null,null);
        var before=peers.LastOrDefault(b=>b.StartTime<current.StartTime);
        var after=peers.FirstOrDefault(b=>b.StartTime>current.StartTime);
        if(!VenueLocationPolicy.HasCoordinatePair(latitude,longitude))
            return("Verified venue coordinates are needed to check travel feasibility.",null);
        var lat=latitude!.Value;var lng=longitude!.Value;
        var quote=_distance.Calculate(lat,lng);
        if(!quote.IsServiceable)return("Venue is outside the serviceable area.",null);
        if(before!=null){
            if(!VenueLocationPolicy.HasCoordinatePair(before.VenueLatitude,before.VenueLongitude))
                return("The preceding event has no verified coordinates.",null);
            var prior=_distance.Calculate(before.VenueLatitude!.Value,before.VenueLongitude!.Value,lat,lng);
            if(!prior.IsServiceable||prior.EstimatedTravelTimeMinutes>(current.StartTime-before.EndTime).TotalMinutes)
                return("Not enough travel time from the previous event.",null);
            // The prior-event route is only a travel-feasibility check.
            // Do not replace the venue's official office-to-event fee with that route.
        }
        if(after!=null){
            if(!VenueLocationPolicy.HasCoordinatePair(after.VenueLatitude,after.VenueLongitude))
                return("The next event has no verified coordinates.",null);
            var next=_distance.Calculate(lat,lng,after.VenueLatitude!.Value,after.VenueLongitude!.Value);
            if(!next.IsServiceable||next.EstimatedTravelTimeMinutes>(after.StartTime-end).TotalMinutes)
                return("Not enough travel time before the following event.",null);
        }
        return(null,quote);
    }
}
