using System.Text.Json;
using System.Text.RegularExpressions;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using Microsoft.EntityFrameworkCore;

internal static class PaymentWindowFixtureAdjust
{
    public static async Task RunAsync()
    {
        var statePath = Path.Combine(Directory.GetCurrentDirectory(),
            ".tmp-owner-build", "unified-client-fixture.json");
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(statePath));
        var state = json.RootElement;
        var database = state.GetProperty("Database").GetString() ?? "";
        var url = state.GetProperty("BaseUrl").GetString();
        var id = state.GetProperty("PayableId").GetInt32();
        if (url != "http://127.0.0.1:5310"
            || !Regex.IsMatch(database, @"^AriesBookingVisual_[a-f0-9]{32}$")
            || id <= 0)
            throw new InvalidOperationException("Only the active, isolated loopback fixture may be aged.");

        var connection = $@"Server=(localdb)\MSSQLLocalDB;Database={database};Integrated Security=True;TrustServerCertificate=True";
        await using var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options);
        var booking = await db.Bookings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (booking?.Status != BookingStatus.AwaitingDownpayment
            || await db.Payments.AnyAsync(p => p.BookingId == id))
            throw new InvalidOperationException("Fixture booking changed; cannot age payment marker.");

        var marker = await db.BookingTimelines.SingleAsync(t => t.BookingId == id
            && t.EventType == TimelineEventType.PaymentWindowOpened);
        marker.CreatedAt = DateTime.UtcNow.AddHours(-25);
        await db.SaveChangesAsync();
        Console.WriteLine("ISOLATED_PAYMENT_WINDOW_AGED_FOR_LATE_POST_TEST");
    }
}
