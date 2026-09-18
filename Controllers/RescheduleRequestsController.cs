using System.Data;
using System.Security.Claims;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AriesMagicAppointmentSystem.Controllers
{
    [Authorize]
    public class RescheduleRequestsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IEmailService _emailService;
        private readonly UserManager<ApplicationUser> _userManager;

        public RescheduleRequestsController(
            ApplicationDbContext context,
            IEmailService emailService,
            UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _emailService = emailService;
            _userManager = userManager;
        }

        [Authorize(Roles = "Client")]
        public async Task<IActionResult> Create()
        {
            var appUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var viewModel = new RescheduleRequestCreateViewModel
            {
                RequestedDate = DateTime.Today,
                Bookings = await GetEligibleClientBookingsAsync(appUserId)
            };

            ViewBag.UnavailableDates = await GetUnavailableRescheduleDatesAsync();

            return View(viewModel);
        }

        [HttpPost]
        [Authorize(Roles = "Client")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(RescheduleRequestCreateViewModel model)
        {
            var appUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            ViewBag.UnavailableDates = await GetUnavailableRescheduleDatesAsync();

            if (model.RequestedDate.Date < DateTime.Today.AddDays(3))
            {
                ModelState.AddModelError("RequestedDate", "Reschedule requests must be made at least 3 days in advance.");
            }
            if (model.RequestedDate.Date == DateTime.Today && model.RequestedStartTime < DateTime.Now.TimeOfDay)
            {
                ModelState.AddModelError("RequestedStartTime", "Past time is not allowed for today.");
            }

            if (!ModelState.IsValid)
            {
                model.Bookings = await GetEligibleClientBookingsAsync(appUserId);
                return View(model);
            }

            var booking = await _context.Bookings
                .Include(b => b.Service)
                .FirstOrDefaultAsync(b => b.Id == model.BookingId && b.ApplicationUserId == appUserId);

            if (booking == null)
            {
                ModelState.AddModelError("", "Invalid booking selected.");
                model.Bookings = await GetEligibleClientBookingsAsync(appUserId);
                return View(model);
            }

            var hasPendingRequest = await _context.RescheduleRequests
                .AnyAsync(r => r.BookingId == booking.Id && r.Status == RescheduleRequestStatus.Pending);

            if (hasPendingRequest)
            {
                ModelState.AddModelError("BookingId", "This booking already has a pending reschedule request.");
                model.Bookings = await GetEligibleClientBookingsAsync(appUserId);
                return View(model);
            }

            var requestedStart = model.RequestedDate.Date.Add(model.RequestedStartTime);
            var requestedEnd = requestedStart.AddHours(booking.Service!.DurationInHours);
            var availability = await CheckRequestedScheduleAvailabilityAsync(
                booking.Id,
                requestedStart,
                requestedEnd);

            if (!availability.IsAvailable)
            {
                ModelState.AddModelError("RequestedStartTime", availability.Message);
                model.Bookings = await GetEligibleClientBookingsAsync(appUserId);
                return View(model);
            }

            var request = new RescheduleRequest
            {
                BookingId = booking.Id,
                OriginalDate = booking.EventDate.Date,
                OriginalStartTime = booking.StartTime,
                OriginalEndTime = booking.EndTime,
                RequestedDate = model.RequestedDate.Date,
                RequestedStartTime = requestedStart,
                RequestedEndTime = requestedEnd,
                Reason = model.Reason,
                Status = RescheduleRequestStatus.Pending,
                CreatedAt = DateTime.Now
            };

            _context.RescheduleRequests.Add(request);

            _context.BookingTimelines.Add(new BookingTimeline
            {
                BookingId = booking.Id,
                EventType = TimelineEventType.RescheduleRequested,
                Notes = "Client submitted a reschedule request.",
                CreatedAt = DateTime.Now
            });

            await _context.SaveChangesAsync();

            var staffUsers = await _userManager.GetUsersInRoleAsync("Staff");

            foreach (var staff in staffUsers)
            {
                await CreateNotificationAsync(
                    staff.Id,
                    "New Reschedule Request",
                    $"Booking BK-{booking.CreatedAt.Year}-{booking.Id:D3} has a new reschedule request.",
                    "/RescheduleRequests/Index");
            }

            TempData["Success"] = "Your reschedule request was submitted successfully. Please wait for Staff review.";
            return RedirectToAction(nameof(MyRequests));
        }

        [Authorize(Roles = "Client")]
        public async Task<IActionResult> MyRequests()
        {
            var appUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var requests = await _context.RescheduleRequests
                .Include(r => r.Booking)
                    .ThenInclude(b => b!.Service)
                .Where(r => r.Booking != null && r.Booking.ApplicationUserId == appUserId)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            return View(requests);
        }

        [Authorize(Roles = "Staff,Owner")]
        public async Task<IActionResult> Index(
            string status = RescheduleRequestStatus.Pending,
            string? search = null,
            DateTime? requestedDate = null,
            string sortBy = "newest")
        {
            var allowedStatuses = new[]
            {
                RescheduleRequestStatus.Pending,
                RescheduleRequestStatus.Approved,
                RescheduleRequestStatus.Rejected,
                "All"
            };
            if (!allowedStatuses.Contains(status, StringComparer.OrdinalIgnoreCase))
            {
                status = RescheduleRequestStatus.Pending;
            }

            var baseQuery = _context.RescheduleRequests
                .AsNoTracking()
                .Include(r => r.Booking)
                    .ThenInclude(b => b!.Client)
                .Include(r => r.Booking)
                    .ThenInclude(b => b!.Service)
                .AsQueryable();

            var model = new RescheduleRequestIndexViewModel
            {
                StatusFilter = status,
                Search = search?.Trim() ?? string.Empty,
                RequestedDate = requestedDate,
                SortBy = string.Equals(sortBy, "oldest", StringComparison.OrdinalIgnoreCase) ? "oldest" : "newest",
                PendingCount = await baseQuery.CountAsync(r => r.Status == RescheduleRequestStatus.Pending),
                ApprovedCount = await baseQuery.CountAsync(r => r.Status == RescheduleRequestStatus.Approved),
                RejectedCount = await baseQuery.CountAsync(r => r.Status == RescheduleRequestStatus.Rejected)
            };

            var query = baseQuery;
            if (!string.Equals(status, "All", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(r => r.Status == status);
            }

            if (!string.IsNullOrWhiteSpace(model.Search))
            {
                var term = model.Search;
                var digits = new string(term.Where(char.IsDigit).ToArray());
                var hasNumericId = int.TryParse(digits, out var numericId);
                query = query.Where(r =>
                    (r.Booking != null && r.Booking.Client != null &&
                        (r.Booking.Client.FullName.Contains(term) || r.Booking.Client.Email.Contains(term))) ||
                    (r.Booking != null &&
                        (r.Booking.PackageName.Contains(term) || (r.Booking.Service != null && r.Booking.Service.Name.Contains(term)))) ||
                    (hasNumericId && (r.Id == numericId || r.BookingId == numericId)));
            }

            if (requestedDate.HasValue)
            {
                query = query.Where(r => r.RequestedDate.Date == requestedDate.Value.Date);
            }

            query = model.SortBy == "oldest"
                ? query.OrderBy(r => r.CreatedAt)
                : query.OrderByDescending(r => r.CreatedAt);

            var requests = await query.ToListAsync();
            foreach (var request in requests)
            {
                var availability = request.Status == RescheduleRequestStatus.Pending
                    ? await CheckRequestedScheduleAvailabilityAsync(request.BookingId, request.RequestedStartTime, request.RequestedEndTime)
                    : new RescheduleAvailabilityViewModel
                    {
                        IsAvailable = false,
                        State = "Processed",
                        Message = "This request has already been processed."
                    };

                model.Requests.Add(new RescheduleRequestReviewItemViewModel
                {
                    Request = request,
                    Availability = availability
                });
            }

            return View(model);
        }

        [Authorize(Roles = "Staff,Owner")]
        public async Task<IActionResult> Details(int? id, string? decision = null)
        {
            if (id == null) return NotFound();

            var request = await _context.RescheduleRequests
                .AsNoTracking()
                .Include(r => r.Booking)
                    .ThenInclude(b => b!.Client)
                .Include(r => r.Booking)
                    .ThenInclude(b => b!.Service)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (request == null || request.Booking == null) return NotFound();

            var availability = request.Status == RescheduleRequestStatus.Pending
                ? await CheckRequestedScheduleAvailabilityAsync(request.BookingId, request.RequestedStartTime, request.RequestedEndTime)
                : new RescheduleAvailabilityViewModel
                {
                    IsAvailable = false,
                    State = "Processed",
                    Message = "This reschedule request has already been processed."
                };

            return View(new RescheduleRequestDetailsViewModel
            {
                Request = request,
                Availability = availability,
                Decision = decision
            });
        }

        [Authorize(Roles = "Staff")]
        public IActionResult Approve(int? id)
        {
            if (id == null) return NotFound();
            return RedirectToAction(nameof(Details), new { id, decision = "approve" });
        }

        [HttpPost, ActionName("Approve")]
        [Authorize(Roles = "Staff")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveConfirmed(int id, string? internalNote)
        {
            var reviewer = await _userManager.GetUserAsync(User);
            var reviewerId = reviewer?.Id ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "Unknown";
            var reviewerName = reviewer?.FullName ?? reviewer?.Email ?? User.Identity?.Name ?? "Staff";
            RescheduleRequest? request;

            await using (var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
            {
                request = await _context.RescheduleRequests
                    .Include(r => r.Booking)
                        .ThenInclude(b => b!.Client)
                    .Include(r => r.Booking)
                        .ThenInclude(b => b!.Service)
                    .FirstOrDefaultAsync(r => r.Id == id);

                if (request == null || request.Booking == null) return NotFound();

                if (request.Status != RescheduleRequestStatus.Pending)
                {
                    await transaction.RollbackAsync();
                    TempData["Error"] = "This reschedule request has already been processed.";
                    return RedirectToAction(nameof(Details), new { id });
                }

                var availability = await CheckRequestedScheduleAvailabilityAsync(
                    request.BookingId,
                    request.RequestedStartTime,
                    request.RequestedEndTime);

                if (!availability.IsAvailable)
                {
                    await transaction.RollbackAsync();
                    TempData["Error"] = $"Schedule Conflict Detected. {availability.Message}";
                    return RedirectToAction(nameof(Details), new { id });
                }

                var reviewedAt = DateTime.Now;
                var originalDate = request.OriginalDate ?? request.Booking.EventDate.Date;
                var originalStartTime = request.OriginalStartTime ?? request.Booking.StartTime;
                var originalEndTime = request.OriginalEndTime ?? request.Booking.EndTime;
                var claimed = await _context.RescheduleRequests
                    .Where(r => r.Id == id && r.Status == RescheduleRequestStatus.Pending)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(r => r.Status, RescheduleRequestStatus.Approved)
                        .SetProperty(r => r.ReviewedAt, reviewedAt)
                        .SetProperty(r => r.ReviewedByUserId, reviewerId)
                        .SetProperty(r => r.ReviewedByName, reviewerName)
                        .SetProperty(r => r.InternalNote, internalNote)
                        .SetProperty(r => r.OriginalDate, originalDate)
                        .SetProperty(r => r.OriginalStartTime, originalStartTime)
                        .SetProperty(r => r.OriginalEndTime, originalEndTime));

                if (claimed != 1)
                {
                    await transaction.RollbackAsync();
                    TempData["Error"] = "This reschedule request has already been processed.";
                    return RedirectToAction(nameof(Details), new { id });
                }

                // Booking is the single live schedule record used by Calendar, Upcoming Events,
                // booking details, reports, client views, and communication booking context.
                request.Booking.EventDate = request.RequestedDate.Date;
                request.Booking.StartTime = request.RequestedStartTime;
                request.Booking.EndTime = request.RequestedEndTime;

                _context.BookingTimelines.Add(new BookingTimeline
                {
                    BookingId = request.Booking.Id,
                    EventType = TimelineEventType.RescheduleApproved,
                    Notes = $"Staff approved reschedule request. New schedule: {request.RequestedDate:MMM dd, yyyy}, {request.RequestedStartTime:hh:mm tt} - {request.RequestedEndTime:hh:mm tt}.",
                    CreatedAt = reviewedAt
                });

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }

            var bookingCode = $"BK-{request!.Booking!.CreatedAt.Year}-{request.Booking.Id:D3}";
            var newSchedule = $"{request.RequestedDate:MMMM dd, yyyy}, {request.RequestedStartTime:hh:mm tt} - {request.RequestedEndTime:hh:mm tt}";

            if (!string.IsNullOrWhiteSpace(request.Booking.ApplicationUserId))
            {
                await CreateNotificationAsync(
                    request.Booking.ApplicationUserId,
                    "Reschedule Approved",
                    $"Your booking has been moved to {newSchedule}.",
                    "/Bookings/MyBookings");
            }

            await NotifyInternalUsersAsync(
                reviewerId,
                "Booking Rescheduled",
                $"{bookingCode} has been rescheduled to {newSchedule}.",
                $"/Bookings/Details/{request.Booking.Id}");

            if (request.Booking.Client != null)
            {
                await _emailService.SendEmailAsync(
                    request.Booking.Client.Email,
                    "Reschedule Approved",
                    $@"
                    <h2>Reschedule Approved</h2>
                    <p>Your event schedule has been updated.</p>
                    <p><strong>{newSchedule}</strong></p>
                    <p>You can view the updated booking details in your account.</p>");
            }

            TempData["Success"] = $"{bookingCode} was rescheduled successfully.";
            return RedirectToAction(nameof(Index));
        }

        [Authorize(Roles = "Staff")]
        public IActionResult Reject(int? id)
        {
            if (id == null) return NotFound();
            return RedirectToAction(nameof(Details), new { id, decision = "reject" });
        }

        [HttpPost, ActionName("Reject")]
        [Authorize(Roles = "Staff")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectConfirmed(int id, string? clientFacingReason, string? internalNote)
        {
            var reviewer = await _userManager.GetUserAsync(User);
            var reviewerId = reviewer?.Id ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "Unknown";
            var reviewerName = reviewer?.FullName ?? reviewer?.Email ?? User.Identity?.Name ?? "Staff";
            RescheduleRequest? request;

            await using (var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
            {
                request = await _context.RescheduleRequests
                    .Include(r => r.Booking)
                        .ThenInclude(b => b!.Client)
                    .FirstOrDefaultAsync(r => r.Id == id);

                if (request == null || request.Booking == null) return NotFound();

                if (request.Status != RescheduleRequestStatus.Pending)
                {
                    await transaction.RollbackAsync();
                    TempData["Error"] = "This reschedule request has already been processed.";
                    return RedirectToAction(nameof(Details), new { id });
                }

                var reviewedAt = DateTime.Now;
                var snapshotOriginalDate = request.OriginalDate ?? request.Booking.EventDate.Date;
                var snapshotOriginalStartTime = request.OriginalStartTime ?? request.Booking.StartTime;
                var snapshotOriginalEndTime = request.OriginalEndTime ?? request.Booking.EndTime;
                var claimed = await _context.RescheduleRequests
                    .Where(r => r.Id == id && r.Status == RescheduleRequestStatus.Pending)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(r => r.Status, RescheduleRequestStatus.Rejected)
                        .SetProperty(r => r.ReviewedAt, reviewedAt)
                        .SetProperty(r => r.ReviewedByUserId, reviewerId)
                        .SetProperty(r => r.ReviewedByName, reviewerName)
                        .SetProperty(r => r.InternalNote, internalNote)
                        .SetProperty(r => r.ClientFacingReason, clientFacingReason)
                        .SetProperty(r => r.OriginalDate, snapshotOriginalDate)
                        .SetProperty(r => r.OriginalStartTime, snapshotOriginalStartTime)
                        .SetProperty(r => r.OriginalEndTime, snapshotOriginalEndTime));

                if (claimed != 1)
                {
                    await transaction.RollbackAsync();
                    TempData["Error"] = "This reschedule request has already been processed.";
                    return RedirectToAction(nameof(Details), new { id });
                }

                // Rejection intentionally does not modify Booking.EventDate/StartTime/EndTime.
                _context.BookingTimelines.Add(new BookingTimeline
                {
                    BookingId = request.Booking.Id,
                    EventType = TimelineEventType.RescheduleRejected,
                    Notes = "Staff rejected the reschedule request. The booking schedule remains unchanged.",
                    CreatedAt = reviewedAt
                });

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }

            var originalDate = request!.OriginalDate ?? request.Booking!.EventDate;
            var originalStart = request.OriginalStartTime ?? request.Booking.StartTime;
            var originalEnd = request.OriginalEndTime ?? request.Booking.EndTime;
            var originalSchedule = $"{originalDate:MMMM dd, yyyy}, {originalStart:hh:mm tt} - {originalEnd:hh:mm tt}";

            if (!string.IsNullOrWhiteSpace(request.Booking.ApplicationUserId))
            {
                await CreateNotificationAsync(
                    request.Booking.ApplicationUserId,
                    "Reschedule Request Declined",
                    $"Your original event schedule remains {originalSchedule}.",
                    "/RescheduleRequests/MyRequests");
            }

            if (request.Booking.Client != null)
            {
                var reasonParagraph = string.IsNullOrWhiteSpace(clientFacingReason)
                    ? string.Empty
                    : $"<p><strong>Reason:</strong> {System.Net.WebUtility.HtmlEncode(clientFacingReason)}</p>";
                await _emailService.SendEmailAsync(
                    request.Booking.Client.Email,
                    "Reschedule Request Declined",
                    $@"
                    <h2>Reschedule Request Declined</h2>
                    <p>Your requested schedule change was not approved.</p>
                    <p>Your original event schedule remains <strong>{originalSchedule}</strong>.</p>
                    {reasonParagraph}");
            }

            TempData["Success"] = "Reschedule request rejected. The booking schedule was not changed.";
            return RedirectToAction(nameof(Index));
        }

        private async Task<List<SelectListItem>> GetEligibleClientBookingsAsync(string? appUserId)
        {
            return await _context.Bookings
                .Include(b => b.Service)
                .Where(b => b.ApplicationUserId == appUserId
                         && (b.Status == BookingStatus.Confirmed
                             || b.Status == BookingStatus.AwaitingDownpayment
                             || b.Status == BookingStatus.AwaitingVerification))
                .Select(b => new SelectListItem
                {
                    Value = b.Id.ToString(),
                    Text = b.Service!.Name + " - " + b.EventDate.ToString("MMM dd, yyyy")
                })
                .ToListAsync();
        }

        private async Task<bool> HasBookingConflictExcludingCurrentBooking(int currentBookingId, DateTime requestedStart, DateTime requestedEnd)
        {
            var confirmedBookings = await _context.Bookings
                .Where(b => b.Status == BookingStatus.Confirmed && b.Id != currentBookingId)
                .ToListAsync();

            var requestedEndWithBuffer = requestedEnd.AddHours(1);
            foreach (var booking in confirmedBookings)
            {
                var existingStart = booking.StartTime;
                var existingEndWithBuffer = booking.EndTime.AddHours(1);

                var overlaps = requestedStart < existingEndWithBuffer && requestedEndWithBuffer > existingStart;

                if (overlaps)
                {
                    return true;
                }
            }

            return false;
        }

        private async Task<RescheduleAvailabilityViewModel> CheckRequestedScheduleAvailabilityAsync(
            int currentBookingId,
            DateTime requestedStart,
            DateTime requestedEnd)
        {
            if (requestedEnd <= requestedStart)
            {
                return new RescheduleAvailabilityViewModel
                {
                    IsAvailable = false,
                    State = "Invalid Schedule",
                    Message = "Requested end time must be later than the requested start time."
                };
            }

            if (requestedStart <= DateTime.Now)
            {
                return new RescheduleAvailabilityViewModel
                {
                    IsAvailable = false,
                    State = "Unavailable",
                    Message = "The requested schedule has already passed."
                };
            }

            var blockedEntry = await _context.BlockedDates
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Date.Date == requestedStart.Date);
            if (blockedEntry != null)
            {
                return new RescheduleAvailabilityViewModel
                {
                    IsAvailable = false,
                    State = "Blocked",
                    Message = string.IsNullOrWhiteSpace(blockedEntry.Reason)
                        ? "The requested date is blocked and unavailable."
                        : $"The requested date is blocked: {blockedEntry.Reason}"
                };
            }

            var maxPerDay = BookingRules.MaximumDailyBookings;
            var confirmedCount = await _context.Bookings
                .CountAsync(b => b.Status == BookingStatus.Confirmed
                            && b.Id != currentBookingId
                            && b.EventDate.Date == requestedStart.Date);

            if (confirmedCount >= maxPerDay)
            {
                return new RescheduleAvailabilityViewModel
                {
                    IsAvailable = false,
                    State = "Daily Limit Reached",
                    Message = "The requested date has reached the maximum number of confirmed bookings."
                };
            }

            if (await HasBookingConflictExcludingCurrentBooking(currentBookingId, requestedStart, requestedEnd))
            {
                return new RescheduleAvailabilityViewModel
                {
                    IsAvailable = false,
                    State = "Schedule Conflict",
                    Message = "Requested schedule overlaps another confirmed event or its required 1-hour buffer."
                };
            }

            return new RescheduleAvailabilityViewModel
            {
                IsAvailable = true,
                State = "Available",
                Message = "Requested schedule is currently available."
            };
        }

        private async Task CreateNotificationAsync(string userId, string title, string message, string? link = null)
        {
            _context.Notifications.Add(new Notification
            {
                UserId = userId,
                Title = title,
                Message = message,
                Link = link,
                IsRead = false,
                CreatedAt = DateTime.Now
            });

            await _context.SaveChangesAsync();
        }

        private async Task NotifyInternalUsersAsync(string skipUserId, string title, string message, string link)
        {
            var staffUsers = await _userManager.GetUsersInRoleAsync("Staff");
            var ownerUsers = await _userManager.GetUsersInRoleAsync("Owner");

            var recipients = staffUsers
                .Concat(ownerUsers)
                .Where(u => u.Id != skipUserId)
                .GroupBy(u => u.Id)
                .Select(g => g.First());

            foreach (var recipient in recipients)
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = recipient.Id,
                    Title = title,
                    Message = message,
                    Link = link,
                    IsRead = false,
                    CreatedAt = DateTime.Now
                });
            }

            await _context.SaveChangesAsync();
        }

        private async Task<List<string>> GetUnavailableRescheduleDatesAsync()
        {
            // Only owner/admin blocked dates are disabled globally. Capacity depends on the
            // selected booking because the current booking must be excluded from the count.
            var blockedDates = await _context.BlockedDates
                .AsNoTracking()
                .Select(x => x.Date.Date)
                .ToListAsync();

            return blockedDates.Select(d => d.ToString("yyyy-MM-dd")).ToList();
        }

        [Authorize(Roles = "Client")]
        [HttpGet]
        public async Task<IActionResult> CheckRescheduleDateAvailability(DateTime date, int? bookingId = null)
        {
            var blockedEntry = await _context.BlockedDates
                .FirstOrDefaultAsync(x => x.Date.Date == date.Date);

            var isBlocked = blockedEntry != null;
            var blockReason = blockedEntry?.Reason;

            var maxPerDay = BookingRules.MaximumDailyBookings;

            var confirmedCount = await _context.Bookings
                .CountAsync(b => b.Status == BookingStatus.Confirmed
                            && b.EventDate.Date == date.Date
                            && (!bookingId.HasValue || b.Id != bookingId.Value));

            var remainingSlots = Math.Max(0, maxPerDay - confirmedCount);

            return Json(new
            {
                isBlocked,
                blockReason,
                maxPerDay,
                confirmedCount,
                remainingSlots,
                isFull = confirmedCount >= maxPerDay
            });
        }
        [Authorize(Roles = "Client")]
        [HttpGet]
        public async Task<IActionResult> GetUnavailableTimeRanges(DateTime date, int? excludeBookingId = null)
        {
            var confirmedBookings = await _context.Bookings
                .Where(b => b.Status == BookingStatus.Confirmed
                        && b.EventDate.Date == date.Date
                        && (!excludeBookingId.HasValue || b.Id != excludeBookingId.Value))
                .OrderBy(b => b.StartTime)
                .ToListAsync();

            var ranges = confirmedBookings.Select(b => new
            {
                start = b.StartTime.ToString("HH:mm"),
                end = b.EndTime.AddHours(1).ToString("HH:mm"),
                display = $"{b.StartTime:hh:mm tt} - {b.EndTime.AddHours(1):hh:mm tt}"
            }).ToList();

            return Json(ranges);
        }
    }
}