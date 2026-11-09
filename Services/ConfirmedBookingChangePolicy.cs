using System.Text.Json;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace AriesMagicAppointmentSystem.Services;

/// <summary>
/// Append-only post-verification amendments. A proposal never mutates a paid booking;
/// a Client's explicit acceptance applies a locked quote under a serializable transaction.
/// Existing BookingTimelines and Notifications tables are reused (no schema change).
/// </summary>
public sealed class ConfirmedBookingChangeProposal
{
    public Guid Token { get; set; }
    public int BookingId { get; set; }
    public string ProposedByUserId { get; set; } = "";
    public DateTime ProposedAtUtc { get; set; }
    public string Reason { get; set; } = "";
    public int OriginalServiceId { get; set; }
    public string OriginalPackageName { get; set; } = "";
    public string OriginalVenue { get; set; } = "";
    public decimal OriginalBasePrice { get; set; }
    public decimal OriginalFinalPrice { get; set; }
    public decimal OriginalTravelFee { get; set; }
    public DateTime OriginalEndTime { get; set; }
    public DateTime OriginalEventDate { get; set; }
    public DateTime OriginalStartTime { get; set; }
    public double? OriginalLatitude { get; set; }
    public double? OriginalLongitude { get; set; }
    public string? OriginalServiceZone { get; set; }
    public string? OriginalTravelFeeStatus { get; set; }
    public string? OriginalRemovedInclusionsJson { get; set; }
    public decimal VerifiedPaymentsAtProposal { get; set; }
    public int VerifiedPaymentCountAtProposal { get; set; }
    public int NewServiceId { get; set; }
    public string NewPackageName { get; set; } = "";
    public decimal NewBasePrice { get; set; }
    public decimal NewFinalPrice { get; set; }
    public decimal NewTravelFee { get; set; }
    public string NewVenue { get; set; } = "";
    public double? NewLatitude { get; set; }
    public double? NewLongitude { get; set; }
    public double? NewDistanceKm { get; set; }
    public string? NewServiceZone { get; set; }
    public bool NewRequiresManualReview { get; set; }
    public DateTime NewEndTime { get; set; }
    public string? NewRemovedInclusionsJson { get; set; }
    public bool VenueChanged => !string.Equals(OriginalVenue, NewVenue, StringComparison.OrdinalIgnoreCase);
    public bool PackageChanged => OriginalServiceId != NewServiceId;
    public decimal NewRemainingBalance => Math.Max(0m, NewFinalPrice - VerifiedPaymentsAtProposal);
}

/// <summary>Shared status and quote guards used by GET, POST and Client acceptance.</summary>
public static class ConfirmedBookingChangePolicy
{
    public const string Proposed = "ConfirmedChangeProposed";
    public const string Accepted = "ConfirmedChangeAccepted";
    public const string Rejected = "ConfirmedChangeRejected";
    public const string Withdrawn = "ConfirmedChangeWithdrawn";
    private static readonly string[] Events = { Proposed, Accepted, Rejected, Withdrawn };

    public static bool MayStart(Booking b, DateTime now)
        => b.Status == BookingStatus.Confirmed
           && b.StartTime > now && !b.IsCompletedLocked
           && !b.RefundEligible && b.CancelledAtUtc == null
           && b.Payments.Any(p => p.Status == PaymentStatus.Verified)
           && !b.Payments.Any(p => p.Status is PaymentStatus.Pending
               or PaymentStatus.AdditionalEvidenceRequired)
           && !b.RescheduleRequests.Any(r => r.Status == RescheduleRequestStatus.Pending);

    public static bool OriginalMatches(Booking b, ConfirmedBookingChangeProposal p) =>
        b.Id == p.BookingId && b.ServiceId == p.OriginalServiceId
        && b.PackageName == p.OriginalPackageName
        && b.PartyVenue == p.OriginalVenue
        && b.BasePrice == p.OriginalBasePrice
        && b.FinalPrice == p.OriginalFinalPrice
        && b.TravelFee == p.OriginalTravelFee
        && b.EndTime == p.OriginalEndTime
        && b.EventDate == p.OriginalEventDate && b.StartTime == p.OriginalStartTime
        && b.VenueLatitude == p.OriginalLatitude && b.VenueLongitude == p.OriginalLongitude
        && b.ServiceZone == p.OriginalServiceZone && b.TravelFeeStatus == p.OriginalTravelFeeStatus
        && b.RemovedInclusionsJson == p.OriginalRemovedInclusionsJson;

    public static bool IsSafeTotal(decimal newTotal, decimal verified)
        => newTotal >= 0m && newTotal >= verified && newTotal <= 999999m;

    public static async Task<ConfirmedBookingChangeProposal?> PendingAsync(
        ApplicationDbContext db, int bookingId, CancellationToken token = default)
    {
        var events = await db.BookingTimelines.AsNoTracking()
            .Where(t => t.BookingId == bookingId && Events.Contains(t.EventType))
            .OrderByDescending(t => t.Id)
            .Select(t => new { t.EventType, t.Notes })
            .Take(60).ToListAsync(token);
        var completed = new HashSet<Guid>();
        foreach (var item in events) // descending ID; resolution events appear before their proposals
        {
            if (item.EventType != Proposed)
            {
                if (Guid.TryParse(item.Notes, out var resolved)) completed.Add(resolved);
                continue;
            }
            try
            {
                var parsed = JsonSerializer.Deserialize<ConfirmedBookingChangeProposal>(item.Notes ?? "");
                if (parsed != null && parsed.BookingId == bookingId && parsed.Token != Guid.Empty
                    && !completed.Contains(parsed.Token)) return parsed;
            }
            catch (JsonException) { /* A malformed event is never auto-applied. */ }
        }
        return null;
    }
}
