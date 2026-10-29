using System.Security.Claims;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AriesMagicAppointmentSystem.ViewComponents;

// Read-only presentation data for choices already offered by the controller. No route or eligibility decision.
public sealed class BookingContextViewComponent(ApplicationDbContext db, IPaymentFinancialService financial) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(IEnumerable<string> allowedIds, bool includeFinance = false)
    {
        var uid = HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var ids = allowedIds.Select(value => int.TryParse(value, out var id) ? id : 0).Where(id => id > 0).Distinct().ToArray();
        if (string.IsNullOrEmpty(uid) || !HttpContext.User.IsInRole("Client") || ids.Length == 0)
            return View(Array.Empty<BookingContextItem>());
        var bookings = await db.Bookings.AsNoTracking().Include(b => b.Service)
            .Where(b => ids.Contains(b.Id) && b.ApplicationUserId == uid).ToListAsync();
        var summaries = includeFinance ? await financial.GetSummariesAsync(bookings.Select(b => b.Id)) : new();
        return View(bookings.Select(b => new BookingContextItem(b.Id, $"BK-{b.CreatedAt.Year}-{b.Id:D3}", b.PackageName,
            b.EventDate.ToString("yyyy-MM-dd"), b.StartTime.ToString("HH:mm"), b.EndTime.ToString("HH:mm"), b.EndTime.ToString("yyyy-MM-dd"), b.PartyVenue,
            b.Service?.DurationInHours, summaries.GetValueOrDefault(b.Id)?.BookingTotal,
            summaries.GetValueOrDefault(b.Id)?.TotalVerifiedPayments, summaries.GetValueOrDefault(b.Id)?.RemainingBalance,
            summaries.TryGetValue(b.Id,out var summary)?summary.Ledger.Select(p=>p.PaymentId).DefaultIfEmpty(0).Max():null)).ToArray());
    }
}
public sealed record BookingContextItem(int Id, string Reference, string Package, string Date, string Start, string End, string EndDate,
    string Venue, int? DurationHours, decimal? Total, decimal? Verified, decimal? Balance, int? LatestPaymentId=null);
