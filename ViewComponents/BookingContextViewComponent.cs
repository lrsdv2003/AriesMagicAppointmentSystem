using System.Security.Claims;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Services;
using AriesMagicAppointmentSystem.ViewModels;
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
            summaries.TryGetValue(b.Id,out var summary)?summary.Ledger.Select(p=>p.PaymentId).DefaultIfEmpty(0).Max():null,
            includeFinance && summaries.TryGetValue(b.Id,out summary)
                ? PaymentOptions.Build(summary.BookingTotal, summary.TotalVerifiedPayments)
                : Array.Empty<PaymentOptionViewModel>())).ToArray());
    }
}
public sealed record BookingContextItem(int Id, string Reference, string Package, string Date, string Start, string End, string EndDate,
    string Venue, int? DurationHours, decimal? Total, decimal? Verified, decimal? Balance, int? LatestPaymentId=null,
    IReadOnlyList<PaymentOptionViewModel>? PaymentOptions=null);

public static class PaymentOptions
{
    public static List<PaymentOptionViewModel> Build(decimal bookingTotal, decimal verified)
    {
        const decimal minimum = 2000m;
        if (verified > 0m || bookingTotal < minimum) return new();
        var half = decimal.Round(bookingTotal / 2m, 2, MidpointRounding.AwayFromZero);
        var candidates = new[]
        {
            new PaymentOptionViewModel("Minimum", "Minimum downpayment", minimum, bookingTotal - minimum,
                "Secure your booking with the minimum required downpayment."),
            new PaymentOptionViewModel("Half", "50% of booking total", half, bookingTotal - half,
                "Pay half of the final booking amount now."),
            new PaymentOptionViewModel("Full", "100% of booking total", bookingTotal, 0m,
                "Settle the full booking amount now.")
        };
        return candidates.Where(option => option.Amount >= minimum && option.Amount <= bookingTotal)
            .GroupBy(option => option.Amount).Select(group => group.First()).ToList();
    }
}
