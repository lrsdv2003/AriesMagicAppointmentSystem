using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace AriesMagicAppointmentSystem.Services
{
    public interface IBookingExpirationService
    {
        Task<int> ExpireOverdueBookingsAsync();
    }

    /// <summary>
    /// New approvals have a 24-hour initial proof window, anchored by an immutable UTC timeline event.
    /// Existing bookings without that event are left alone rather than assigned a fabricated deadline.
    /// Pending/verified proof submitted on time prevents expiry while Owner review is in progress.
    /// </summary>
    public class BookingExpirationService : IBookingExpirationService
    {
        private readonly ApplicationDbContext _context;
        private readonly ISystemActivityService _activityService;
        private readonly ILogger<BookingExpirationService> _logger;

        public BookingExpirationService(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            ISystemActivityService activityService,
            ILogger<BookingExpirationService> logger)
        {
            _context = context;
            _activityService = activityService;
            _logger = logger;
        }

        public async Task<int> ExpireOverdueBookingsAsync()
        {
            var currentUtc = DateTime.UtcNow;
            // Read only ids first; each booking and its payment evidence are rechecked inside
            // a SERIALIZABLE transaction to coordinate with PaymentController.Upload.
            var candidateIds = await _context.Bookings.AsNoTracking()
                .Where(b => b.Status == BookingStatus.AwaitingDownpayment && b.ArchivedAt == null)
                .Where(b => _context.BookingTimelines.Any(t =>
                    t.BookingId == b.Id && t.EventType == TimelineEventType.PaymentWindowOpened
                    && t.CreatedAt <= currentUtc.AddHours(-24)))
                .Select(b => b.Id).ToListAsync();

            var count = 0;
            foreach (var id in candidateIds)
            {
                await using var transaction =
                    await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                var booking = await _context.Bookings
                    .Include(b => b.Payments)
                    .FirstOrDefaultAsync(b => b.Id == id);

                if (booking == null || booking.Status != BookingStatus.AwaitingDownpayment
                    || booking.ArchivedAt != null)
                {
                    await transaction.RollbackAsync();
                    continue;
                }

                var deadlineUtc = await InitialPaymentWindowPolicy.GetDeadlineUtcAsync(_context, id);
                if (!InitialPaymentWindowPolicy.IsOverdue(deadlineUtc, DateTime.UtcNow))
                {
                    await transaction.RollbackAsync();
                    continue;
                }

                // A successful in-window submission remains protected while Owner review is pending.
                // Additional-evidence requests likewise retain the original proof record for review.
                if (booking.Payments.Any(p => p.Status is PaymentStatus.Pending
                        or PaymentStatus.Verified or PaymentStatus.AdditionalEvidenceRequired))
                {
                    await transaction.RollbackAsync();
                    continue;
                }

                if (await _context.BookingTimelines.AnyAsync(t =>
                    t.BookingId == id && t.EventType == TimelineEventType.BookingExpired))
                {
                    await transaction.RollbackAsync();
                    continue;
                }

                var nowUtc = DateTime.UtcNow;
                booking.Status = BookingStatus.Expired;
                booking.TrashReason = TrashReason.BookingRequestExpired;
                booking.TrashNotes = "No qualifying initial payment proof was submitted within 24 hours of Staff approval.";
                booking.ArchivedAt = nowUtc;
                _context.BookingTimelines.Add(new BookingTimeline
                {
                    BookingId = id,
                    EventType = TimelineEventType.BookingExpired,
                    Notes = $"24-hour Staff-approval payment window elapsed at {deadlineUtc:O}. "
                          + "No qualifying payment proof remained under review or verified.",
                    CreatedAt = nowUtc
                });

                if (!string.IsNullOrWhiteSpace(booking.ApplicationUserId))
                {
                    _context.Notifications.Add(new Notification
                    {
                        UserId = booking.ApplicationUserId,
                        Title = "Booking Expired",
                        Message = $"Booking BK-{booking.CreatedAt.Year}-{id:D3} expired because no qualifying payment proof was submitted before "
                                  + InitialPaymentWindowPolicy.FormatManila(deadlineUtc!.Value)
                                  + ". Please contact Staff if you need assistance.",
                        Link = $"/Bookings/Details/{id}",
                        IsRead = false,
                        CreatedAt = DateTime.Now
                    });
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                count++;

                try
                {
                    await _activityService.LogAsync(
                        SystemActivityType.BookingExpired,
                        $"Booking BK-{booking.CreatedAt.Year}-{id:D3} expired after the 24-hour initial proof window.",
                        "SYSTEM", "System (Payment Deadline)", id.ToString(), "Booking",
                        new { BookingId = id, DeadlineUtc = deadlineUtc, ExpiredAtUtc = nowUtc });
                }
                catch (Exception error)
                {
                    _logger.LogWarning(error, "Unable to write additional audit activity for expired booking {BookingId}", id);
                }
                _logger.LogInformation("Expired unpaid booking {BookingId} at {DeadlineUtc}", id, deadlineUtc);
            }
            return count;
        }
    }
}
