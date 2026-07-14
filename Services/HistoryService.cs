using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AriesMagicAppointmentSystem.Services
{
    /// <summary>
    /// Business logic for the History (archive) module. Controllers stay thin and only
    /// translate HTTP <-> this service; all archiving/query/sort/filter rules live here so
    /// they are exercised the same way regardless of which page or export triggered them.
    ///
    /// No new tables are introduced. History is simply the existing Booking table filtered
    /// to Status == Completed, with BookingTimeline supplying the read-only audit trail.
    /// </summary>
    public class HistoryService : IHistoryService
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public HistoryService(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<int> ArchiveDueBookingsAsync()
        {
            var now = DateTime.Now;
            var archivedCount = 0;

            // 1) Confirmed bookings whose event has already ended automatically become Completed.
            var overdueConfirmed = await _context.Bookings
                .Where(b => b.Status == BookingStatus.Confirmed && b.EndTime < now)
                .ToListAsync();

            foreach (var booking in overdueConfirmed)
            {
                booking.Status = BookingStatus.Completed;
                booking.IsCompletedLocked = true;

                _context.BookingTimelines.Add(new BookingTimeline
                {
                    BookingId = booking.Id,
                    EventType = TimelineEventType.BookingCompleted,
                    Notes = "Automatically marked as completed because the event date and time has passed.",
                    CreatedAt = now
                });
            }

            // 2) Any Completed booking (auto or manually completed by staff) that doesn't yet
            // have an "Archived" marker gets one now, plus a one-time Owner notification.
            // This is what actually moves a booking into the History module.
            var completedBookingIds = await _context.Bookings
                .Where(b => b.Status == BookingStatus.Completed)
                .Select(b => b.Id)
                .ToListAsync();

            if (completedBookingIds.Count > 0)
            {
                var alreadyArchivedIds = await _context.BookingTimelines
                    .Where(t => completedBookingIds.Contains(t.BookingId) && t.EventType == TimelineEventType.BookingArchived)
                    .Select(t => t.BookingId)
                    .Distinct()
                    .ToListAsync();

                var newlyArchivedIds = completedBookingIds.Except(alreadyArchivedIds).ToList();

                if (newlyArchivedIds.Count > 0)
                {
                    var ownerUsers = await _userManager.GetUsersInRoleAsync("Owner");

                    foreach (var bookingId in newlyArchivedIds)
                    {
                        var bookingCode = await BuildBookingCodeAsync(bookingId);

                        _context.BookingTimelines.Add(new BookingTimeline
                        {
                            BookingId = bookingId,
                            EventType = TimelineEventType.BookingArchived,
                            Notes = "Booking was archived into the History module.",
                            CreatedAt = now
                        });

                        foreach (var owner in ownerUsers)
                        {
                            _context.Notifications.Add(new Notification
                            {
                                UserId = owner.Id,
                                Title = "Booking Archived",
                                Message = $"Booking #{bookingCode} has been archived into History.",
                                Link = $"/History/Details/{bookingId}",
                                IsRead = false,
                                CreatedAt = now
                            });
                        }

                        archivedCount++;
                    }
                }
            }

            if (archivedCount > 0 || overdueConfirmed.Count > 0)
            {
                await _context.SaveChangesAsync();
            }

            return archivedCount;
        }

        public async Task<HistoryIndexViewModel> GetHistoryAsync(HistoryFilterViewModel filters)
        {
            await ArchiveDueBookingsAsync();

            var query = BuildFilteredQuery(filters);
            query = ApplySort(query, filters.SortBy);

            var totalCount = await query.CountAsync();

            var page = filters.Page < 1 ? 1 : filters.Page;
            var pageSize = filters.PageSize < 1 ? 15 : filters.PageSize;

            var pagedBookings = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var rows = await BuildRowsAsync(pagedBookings);

            var viewModel = new HistoryIndexViewModel
            {
                Filters = filters,
                Summary = await GetSummaryAsync(),
                Bookings = rows,
                TotalCount = totalCount,
                TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
                AvailableServices = await _context.Services.OrderBy(s => s.Name).ToListAsync(),
                AvailableEventTypes = await _context.Bookings
                    .Where(b => b.Status == BookingStatus.Completed)
                    .Select(b => b.EventType)
                    .Distinct()
                    .OrderBy(e => e)
                    .ToListAsync()
            };

            return viewModel;
        }

        public async Task<List<HistoryRowViewModel>> GetHistoryForExportAsync(HistoryFilterViewModel filters)
        {
            await ArchiveDueBookingsAsync();

            var query = BuildFilteredQuery(filters);
            query = ApplySort(query, filters.SortBy);

            var bookings = await query.ToListAsync();
            return await BuildRowsAsync(bookings);
        }

        public async Task<HistoryDetailsViewModel?> GetDetailsAsync(int bookingId)
        {
            await ArchiveDueBookingsAsync();

            var booking = await _context.Bookings
                .Include(b => b.Client)
                .Include(b => b.Service)
                .Include(b => b.Payments)
                .Include(b => b.RescheduleRequests)
                .Include(b => b.TimelineEvents)
                .FirstOrDefaultAsync(b => b.Id == bookingId && b.Status == BookingStatus.Completed);

            if (booking == null)
            {
                return null;
            }

            var refund = await _context.RefundRequests
                .Where(r => r.BookingId == bookingId)
                .OrderByDescending(r => r.RequestedAt)
                .FirstOrDefaultAsync();

            var (amountPaid, remainingBalance, paymentStatus) = CalculatePaymentSummary(booking);

            var archivedEntry = booking.TimelineEvents
                .Where(t => t.EventType == TimelineEventType.BookingArchived)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefault();

            var completedEntry = booking.TimelineEvents
                .Where(t => t.EventType == TimelineEventType.BookingCompleted)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefault();

            return new HistoryDetailsViewModel
            {
                Booking = booking,
                BookingCode = $"BK-{booking.CreatedAt.Year}-{booking.Id:D3}",
                AmountPaid = amountPaid,
                RemainingBalance = remainingBalance,
                PaymentStatus = paymentStatus,
                Refund = refund,
                RefundStatus = refund?.Status ?? "None",
                CompletedAt = archivedEntry?.CreatedAt ?? completedEntry?.CreatedAt,
                Timeline = booking.TimelineEvents
                    .OrderBy(t => t.CreatedAt)
                    .Select(t => new HistoryTimelineItemViewModel
                    {
                        EventType = t.EventType,
                        Notes = t.Notes,
                        CreatedAt = t.CreatedAt
                    })
                    .ToList()
            };
        }

        public async Task<HistorySummaryViewModel> GetSummaryAsync()
        {
            var now = DateTime.Now;
            var today = now.Date;
            var monthStart = new DateTime(now.Year, now.Month, 1);
            var yearStart = new DateTime(now.Year, 1, 1);

            var upcomingEvents = await _context.Bookings
                .CountAsync(b => b.Status == BookingStatus.Confirmed && b.EventDate.Date >= today);

            var todaysEvents = await _context.Bookings
                .CountAsync(b => b.Status == BookingStatus.Confirmed && b.EventDate.Date == today);

            var historyCount = await _context.Bookings
                .CountAsync(b => b.Status == BookingStatus.Completed);

            // "Completed this month/year" is measured against the event date, i.e. events that
            // actually happened in that period (not when the archive flag was flipped).
            var completedThisMonth = await _context.Bookings
                .CountAsync(b => b.Status == BookingStatus.Completed && b.EventDate >= monthStart && b.EventDate < monthStart.AddMonths(1));

            var completedThisYear = await _context.Bookings
                .CountAsync(b => b.Status == BookingStatus.Completed && b.EventDate >= yearStart && b.EventDate < yearStart.AddYears(1));

            var lifetimeRevenue = await _context.Payments
                .Where(p => p.Status == PaymentStatus.Verified && p.Booking != null && p.Booking.Status == BookingStatus.Completed)
                .SumAsync(p => (decimal?)p.Amount) ?? 0m;

            return new HistorySummaryViewModel
            {
                UpcomingEvents = upcomingEvents,
                TodaysEvents = todaysEvents,
                HistoryCount = historyCount,
                CompletedThisMonth = completedThisMonth,
                CompletedThisYear = completedThisYear,
                LifetimeEvents = historyCount,
                LifetimeRevenue = lifetimeRevenue
            };
        }

        // ---------------------------------------------------------------------
        // Internal helpers
        // ---------------------------------------------------------------------

        private IQueryable<Booking> BuildFilteredQuery(HistoryFilterViewModel filters)
        {
            var query = _context.Bookings
                .Include(b => b.Client)
                .Include(b => b.Service)
                .Include(b => b.Payments)
                .Where(b => b.Status == BookingStatus.Completed)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(filters.Search))
            {
                var term = filters.Search.Trim().ToLower();

                query = query.Where(b =>
                    b.Id.ToString().Contains(term) ||
                    (b.Client != null && b.Client.FullName != null && b.Client.FullName.ToLower().Contains(term)) ||
                    (b.ContactNumber != null && b.ContactNumber.ToLower().Contains(term)) ||
                    b.PackageName.ToLower().Contains(term) ||
                    (b.Service != null && b.Service.Name.ToLower().Contains(term)) ||
                    b.EventType.ToLower().Contains(term) ||
                    b.PartyVenue.ToLower().Contains(term));
            }

            var now = DateTime.Now;

            switch (filters.DateRange)
            {
                case "Today":
                    query = query.Where(b => b.EventDate.Date == now.Date);
                    break;
                case "ThisWeek":
                    var startOfWeek = now.Date.AddDays(-(int)now.DayOfWeek);
                    var endOfWeek = startOfWeek.AddDays(7);
                    query = query.Where(b => b.EventDate >= startOfWeek && b.EventDate < endOfWeek);
                    break;
                case "ThisMonth":
                    var startOfMonth = new DateTime(now.Year, now.Month, 1);
                    query = query.Where(b => b.EventDate >= startOfMonth && b.EventDate < startOfMonth.AddMonths(1));
                    break;
                case "Year":
                    var year = filters.Year ?? now.Year;
                    query = query.Where(b => b.EventDate.Year == year);
                    break;
            }

            if (filters.CompletedDateFrom.HasValue)
            {
                var from = filters.CompletedDateFrom.Value.Date;
                query = query.Where(b => b.TimelineEvents.Any(t =>
                    t.EventType == TimelineEventType.BookingArchived && t.CreatedAt >= from));
            }

            if (filters.CompletedDateTo.HasValue)
            {
                var to = filters.CompletedDateTo.Value.Date.AddDays(1);
                query = query.Where(b => b.TimelineEvents.Any(t =>
                    t.EventType == TimelineEventType.BookingArchived && t.CreatedAt < to));
            }

            if (filters.ServiceId.HasValue)
            {
                query = query.Where(b => b.ServiceId == filters.ServiceId.Value);
            }

            if (!string.IsNullOrWhiteSpace(filters.EventType) && filters.EventType != "All")
            {
                query = query.Where(b => b.EventType == filters.EventType);
            }

            if (!string.IsNullOrWhiteSpace(filters.PaymentStatus) && filters.PaymentStatus != "All")
            {
                if (filters.PaymentStatus == "No Payment")
                {
                    query = query.Where(b => !b.Payments.Any());
                }
                else
                {
                    query = query.Where(b => b.Payments
                        .OrderByDescending(p => p.UploadedAt)
                        .Select(p => p.Status)
                        .FirstOrDefault() == filters.PaymentStatus);
                }
            }

            if (!string.IsNullOrWhiteSpace(filters.RefundStatus) && filters.RefundStatus != "All")
            {
                if (filters.RefundStatus == "None")
                {
                    query = query.Where(b => !_context.RefundRequests.Any(r => r.BookingId == b.Id));
                }
                else
                {
                    query = query.Where(b => _context.RefundRequests
                        .Where(r => r.BookingId == b.Id)
                        .OrderByDescending(r => r.RequestedAt)
                        .Select(r => r.Status)
                        .FirstOrDefault() == filters.RefundStatus);
                }
            }

            return query;
        }

        private static IQueryable<Booking> ApplySort(IQueryable<Booking> query, string? sortBy)
        {
            return sortBy switch
            {
                "Oldest" => query.OrderBy(b => b.EventDate),
                "HighestRevenue" => query.OrderByDescending(b => b.FinalPrice),
                "LowestRevenue" => query.OrderBy(b => b.FinalPrice),
                "ClientName" => query.OrderBy(b => b.Client != null ? b.Client.FullName : string.Empty),
                "EventDate" => query.OrderBy(b => b.EventDate),
                _ => query.OrderByDescending(b => b.EventDate) // "Newest" default
            };
        }

        private async Task<List<HistoryRowViewModel>> BuildRowsAsync(List<Booking> bookings)
        {
            if (bookings.Count == 0)
            {
                return new List<HistoryRowViewModel>();
            }

            var bookingIds = bookings.Select(b => b.Id).ToList();

            var refundStatusByBooking = await _context.RefundRequests
                .Where(r => bookingIds.Contains(r.BookingId))
                .GroupBy(r => r.BookingId)
                .Select(g => new { BookingId = g.Key, Status = g.OrderByDescending(r => r.RequestedAt).First().Status })
                .ToDictionaryAsync(x => x.BookingId, x => x.Status);

            var archivedAtByBooking = await _context.BookingTimelines
                .Where(t => bookingIds.Contains(t.BookingId) && t.EventType == TimelineEventType.BookingArchived)
                .GroupBy(t => t.BookingId)
                .Select(g => new { BookingId = g.Key, CreatedAt = g.Max(t => t.CreatedAt) })
                .ToDictionaryAsync(x => x.BookingId, x => x.CreatedAt);

            var rows = new List<HistoryRowViewModel>();

            foreach (var booking in bookings)
            {
                var (amountPaid, remainingBalance, paymentStatus) = CalculatePaymentSummary(booking);

                rows.Add(new HistoryRowViewModel
                {
                    Id = booking.Id,
                    BookingCode = $"BK-{booking.CreatedAt.Year}-{booking.Id:D3}",
                    ClientName = booking.Client?.FullName ?? "N/A",
                    ClientPhone = booking.ContactNumber,
                    EventType = booking.EventType,
                    PackageName = booking.PackageName,
                    ServiceName = booking.Service?.Name ?? booking.PackageName,
                    Venue = booking.PartyVenue,
                    EventDate = booking.EventDate,
                    StartTime = booking.StartTime,
                    EndTime = booking.EndTime,
                    Guests = booking.PaxCount,
                    FinalPrice = booking.FinalPrice,
                    AmountPaid = amountPaid,
                    RemainingBalance = remainingBalance,
                    PaymentStatus = paymentStatus,
                    RefundStatus = refundStatusByBooking.TryGetValue(booking.Id, out var refundStatus) ? refundStatus : "None",
                    CompletedAt = archivedAtByBooking.TryGetValue(booking.Id, out var archivedAt) ? archivedAt : null
                });
            }

            return rows;
        }

        private static (decimal AmountPaid, decimal RemainingBalance, string PaymentStatus) CalculatePaymentSummary(Booking booking)
        {
            var amountPaid = booking.Payments
                .Where(p => p.Status == PaymentStatus.Verified)
                .Sum(p => p.Amount);

            var remainingBalance = Math.Max(0, booking.FinalPrice - amountPaid);

            var latestPayment = booking.Payments
                .OrderByDescending(p => p.UploadedAt)
                .FirstOrDefault();

            var paymentStatus = latestPayment?.Status ?? "No Payment";

            return (amountPaid, remainingBalance, paymentStatus);
        }

        private async Task<string> BuildBookingCodeAsync(int bookingId)
        {
            var booking = await _context.Bookings.FirstOrDefaultAsync(b => b.Id == bookingId);
            return booking == null ? bookingId.ToString() : $"{booking.CreatedAt.Year}-{booking.Id:D3}";
        }
    }
}
