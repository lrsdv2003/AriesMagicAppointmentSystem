using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace AriesMagicAppointmentSystem.Services;

/// <summary>
/// Resolves the established default of three confirmed bookings, with a valid
/// Owner-maintained date override. Blocked dates and one-hour conflicts remain
/// independent, higher-priority availability restrictions.
/// </summary>
public static class DateBookingCapacity
{
    public const int MinimumLimit = 1; // Closure is handled by BlockedDates, not a zero limit.
    public const int MaximumLimit = 20; // Existing DateBookingLimit model supported up to 20.

    public static bool IsValid(int count) => count is >= MinimumLimit and <= MaximumLimit;

    public static async Task<int> EffectiveLimitAsync(ApplicationDbContext db, DateTime date)
    {
        var value = await db.DateBookingLimits.AsNoTracking()
            .Where(x => x.Date.Date == date.Date && x.MaxBookings >= MinimumLimit && x.MaxBookings <= MaximumLimit)
            .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Select(x => x.MaxBookings).FirstOrDefaultAsync();
        return value == 0 ? BookingRules.MaximumDailyBookings : value;
    }

    public static Dictionary<DateTime, int> Overrides(IEnumerable<DateBookingLimit> records) =>
        records.Where(x => IsValid(x.MaxBookings))
            .GroupBy(x => x.Date.Date)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.CreatedAt)
                .ThenByDescending(x => x.Id).First().MaxBookings);
}
