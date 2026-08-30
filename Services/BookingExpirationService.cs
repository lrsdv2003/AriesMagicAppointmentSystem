using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AriesMagicAppointmentSystem.Services
{
    public interface IBookingExpirationService
    {
        Task<int> ExpireOverdueBookingsAsync();
    }

    public class BookingExpirationService : IBookingExpirationService
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ISystemActivityService _activityService;
        private readonly ILogger<BookingExpirationService> _logger;

        private static readonly TimeZoneInfo PhilippineZone = GetPhilippineZone();

        private static TimeZoneInfo GetPhilippineZone()
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila"); } catch { }
            try { return TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time"); } catch { }
            return TimeZoneInfo.Local;
        }

        public BookingExpirationService(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            ISystemActivityService activityService,
            ILogger<BookingExpirationService> logger)
        {
            _context = context;
            _userManager = userManager;
            _activityService = activityService;
            _logger = logger;
        }

        public async Task<int> ExpireOverdueBookingsAsync()
        {
            var philippineNow = TimeZoneInfo.ConvertTime(DateTime.UtcNow, PhilippineZone);
            var philippineToday = philippineNow.Date;

            // Only bookings waiting for client payment
            var candidates = await _context.Bookings
                .Include(b => b.Payments)
                .Include(b => b.Client)
                .Where(b => b.Status == BookingStatus.AwaitingDownpayment)
                .Where(b => b.ArchivedAt == null)
                .ToListAsync();

            int expiredCount = 0;

            foreach (var booking in candidates)
            {
                // Business rule: must be created at least 3 days before event (enforced at creation),
                // but guard against edge case where deadline already passed at creation time.
                // If booking was created after deadline, give at least 24h grace before expiring.
                var paymentDeadline = booking.EventDate.Date.AddDays(-3);
                
                // Idempotent: if deadline not yet passed, skip. Use > to allow full deadline day.
                if (philippineToday <= paymentDeadline)
                    continue;

                // If booking was created less than 3 days before event (edge case), deadline is in past relative to creation.
                // Align with existing rule: booking creation requires EventDate >= Today+3, so this should not happen.
                // But if it does, don't trash immediately - require at least 1 day after creation.
                if (booking.CreatedAt.Date.AddDays(1) > philippineToday)
                    continue;

                // Has client submitted valid proof? Any Pending or Verified payment counts as submitted.
                // Rejected does NOT count - client needs to resubmit.
                bool hasValidProof = booking.Payments.Any(p => p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Verified);
                if (hasValidProof)
                    continue;

                // Already expired/cancelled/trashed? Status check above ensures not, but double-check.
                if (booking.Status == BookingStatus.Expired || booking.Status == BookingStatus.Cancelled || booking.Status == BookingStatus.Declined)
                    continue;

                // Check if already has expired timeline to ensure idempotency
                bool alreadyExpired = await _context.BookingTimelines.AnyAsync(t => t.BookingId == booking.Id && t.EventType == TimelineEventType.BookingExpired);
                if (alreadyExpired)
                    continue;

                // Perform auto-trash
                var previousStatus = booking.Status;
                booking.Status = BookingStatus.Expired;
                booking.TrashReason = TrashReason.BookingRequestExpired;
                booking.TrashNotes = "Automatically expired because no proof of payment was submitted by the 3-day payment deadline.";
                booking.ArchivedAt = DateTime.UtcNow;

                _context.BookingTimelines.Add(new BookingTimeline
                {
                    BookingId = booking.Id,
                    EventType = TimelineEventType.BookingExpired,
                    Notes = $"Automatically moved to Trash: no proof of payment by deadline {paymentDeadline:MMMM dd, yyyy} (Reservation: {booking.EventDate:MMMM dd, yyyy}). Previous status: {previousStatus}.",
                    CreatedAt = DateTime.Now
                });

                // Notify client
                if (!string.IsNullOrWhiteSpace(booking.ApplicationUserId))
                {
                    // Avoid duplicate post-trash notification (idempotent)
                    bool alreadyNotified = await _context.Notifications.AnyAsync(n => n.UserId == booking.ApplicationUserId && n.Link == $"/Bookings/MyBookings" && n.Title == "Booking Expired" && n.CreatedAt.Date == philippineToday);
                    if (!alreadyNotified)
                    {
                        _context.Notifications.Add(new Notification
                        {
                            UserId = booking.ApplicationUserId,
                            Title = "Booking Expired",
                            Message = $"Your booking for {booking.EventDate:MMMM dd, yyyy} has been automatically cancelled because no proof of payment was submitted by the required deadline ({paymentDeadline:MMMM dd, yyyy}). The date is now available for other clients.",
                            Link = "/Bookings/MyBookings",
                            IsRead = false,
                            CreatedAt = DateTime.Now
                        });
                    }
                }

                await _context.SaveChangesAsync();

                // System activity audit
                try
                {
                    await _activityService.LogAsync(
                        SystemActivityType.BookingExpired,
                        $"Booking BK-{booking.CreatedAt.Year}-{booking.Id:D3} auto-expired: no proof by deadline {paymentDeadline:MM/dd/yyyy} (Event: {booking.EventDate:MM/dd/yyyy})",
                        "SYSTEM",
                        "System (Auto-Trash)",
                        booking.Id.ToString(),
                        "Booking",
                        new { booking.EventDate, paymentDeadline, booking.CreatedAt, previousStatus });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to log auto-trash activity for booking {BookingId}", booking.Id);
                }

                _logger.LogInformation("Auto-trashed booking {BookingId} (Event {EventDate} deadline {Deadline})", booking.Id, booking.EventDate, paymentDeadline);
                expiredCount++;

                // Optional warnings: 5-day and 3-day pre-deadline warnings
                // Handled in separate pass below to avoid mixing with expiration loop
            }

            // Send pre-deadline warnings (5 days and 3 days before reservation)
            await SendWarningsAsync(philippineToday, candidates);

            return expiredCount;
        }

        private async Task SendWarningsAsync(DateTime philippineToday, List<Booking> candidates)
        {
            foreach (var booking in candidates)
            {
                // Only warn for bookings that are still active and not yet expired (filtered above but re-check)
                if (booking.Status != BookingStatus.AwaitingDownpayment) continue;
                if (booking.Payments.Any(p => p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Verified)) continue;

                var daysUntilEvent = (booking.EventDate.Date - philippineToday).Days;
                var deadline = booking.EventDate.Date.AddDays(-3);

                string? title = null;
                string? message = null;

                if (daysUntilEvent == 5)
                {
                    title = "Payment Reminder: 5 Days Until Reservation";
                    message = $"Your reservation on {booking.EventDate:MMMM dd, yyyy} is approaching. Please submit your proof of payment by {deadline:MMMM dd, yyyy} to keep your reservation.";
                }
                else if (daysUntilEvent == 3)
                {
                    title = "Payment Deadline Approaching";
                    message = $"Your booking for {booking.EventDate:MMMM dd, yyyy} is approaching its payment deadline ({deadline:MMMM dd, yyyy}). Please submit your proof of payment today to keep your reservation.";
                }

                if (title == null || string.IsNullOrWhiteSpace(booking.ApplicationUserId)) continue;

                bool alreadyWarned = await _context.Notifications.AnyAsync(n => n.UserId == booking.ApplicationUserId && n.Title == title && n.Link == "/Bookings/MyBookings" && n.CreatedAt.Date == philippineToday);
                if (alreadyWarned) continue;

                _context.Notifications.Add(new Notification
                {
                    UserId = booking.ApplicationUserId,
                    Title = title,
                    Message = message!,
                    Link = "/Bookings/MyBookings",
                    IsRead = false,
                    CreatedAt = DateTime.Now
                });
            }

            await _context.SaveChangesAsync();
        }
    }
}
