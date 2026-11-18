using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace AriesMagicAppointmentSystem.Services;

/// <summary>
/// Only new, explicitly stamped Staff approvals have a payment window.
/// Existing approval records are intentionally exempt; no historical timestamps are invented.
/// Timestamps for the marker event are always stored in UTC (DateTime.Kind is not persisted by SQL Server).
/// </summary>
public static class InitialPaymentWindowPolicy
{
    public static readonly TimeSpan Duration = TimeSpan.FromHours(24);

    public static DateTime DeadlineFromOpenedUtc(DateTime storedUtc) =>
        DateTime.SpecifyKind(storedUtc, DateTimeKind.Utc).Add(Duration);

    public static async Task<DateTime?> GetDeadlineUtcAsync(ApplicationDbContext db, int bookingId)
    {
        var opened = await db.BookingTimelines.AsNoTracking()
            .Where(t => t.BookingId == bookingId && t.EventType == TimelineEventType.PaymentWindowOpened)
            .OrderBy(t => t.CreatedAt)
            .Select(t => (DateTime?)t.CreatedAt).FirstOrDefaultAsync();
        return opened.HasValue ? DeadlineFromOpenedUtc(opened.Value) : null;
    }

    public static async Task<Dictionary<int, DateTime>> GetDeadlinesUtcAsync(
        ApplicationDbContext db, IEnumerable<int> bookingIds)
    {
        var ids = bookingIds.Distinct().ToArray();
        if (ids.Length == 0) return new();
        var events = await db.BookingTimelines.AsNoTracking()
            .Where(t => ids.Contains(t.BookingId) && t.EventType == TimelineEventType.PaymentWindowOpened)
            .Select(t => new { t.BookingId, t.CreatedAt }).ToListAsync();
        return events.GroupBy(x => x.BookingId).ToDictionary(
            group => group.Key,
            group => DeadlineFromOpenedUtc(group.Min(x => x.CreatedAt)));
    }

    public static bool IsOverdue(DateTime? deadlineUtc, DateTime nowUtc) =>
        deadlineUtc.HasValue && nowUtc >= deadlineUtc.Value;

    public static string FormatManila(DateTime deadlineUtc)
    {
        TimeZoneInfo timeZone;
        try { timeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila"); }
        catch (TimeZoneNotFoundException)
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time");
        }
        var local = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(deadlineUtc, DateTimeKind.Utc), timeZone);
        return $"{local:MMMM d, yyyy 'at' h:mm tt} Philippine time";
    }
}
