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

            if (model.RequestedDate.Date < DateTime.Today)
            {
                ModelState.AddModelError("RequestedDate", "Past dates are not allowed.");
            }

            var isBlocked = await _context.BlockedDates
                .AnyAsync(x => x.Date.Date == model.RequestedDate.Date);

            if (isBlocked)
            {
                ModelState.AddModelError("RequestedDate", "This date is unavailable for reschedule.");
            }

            if (await HasReachedDailyConfirmedLimitForReschedule(model.RequestedDate))
            {
                ModelState.AddModelError("RequestedDate", "This date has already reached the maximum number of bookings.");
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

            var requestedStart = model.RequestedDate.Date.Add(model.RequestedStartTime);
            var requestedEnd = requestedStart.AddHours(booking.Service!.DurationInHours);

            bool conflict = await HasBookingConflictExcludingCurrentBooking(
                booking.Id,
                requestedStart,
                requestedEnd);

            if (conflict)
            {
                ModelState.AddModelError("RequestedStartTime", "The selected time conflicts with an existing confirmed booking.");
                model.Bookings = await GetEligibleClientBookingsAsync(appUserId);
                return View(model);
            }

            var request = new RescheduleRequest
            {
                BookingId = booking.Id,
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
            var adminUsers = await _userManager.GetUsersInRoleAsync("Admin");

            foreach (var staff in staffUsers)
            {
                await CreateNotificationAsync(
                    staff.Id,
                    "New Reschedule Request",
                    "A client submitted a reschedule request.",
                    "/RescheduleRequests/Index");
            }

            foreach (var admin in adminUsers)
            {
                await CreateNotificationAsync(
                    admin.Id,
                    "Reschedule Awaiting Final Approval",
                    "A reschedule request is awaiting final approval.",
                    "/RescheduleRequests/Index");
            }
            TempData["Success"] = "Your reschedule request was submitted successfully. Please wait for admin review.";
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

        [Authorize(Roles = "Staff,Admin")]
        public async Task<IActionResult> Index()
        {
            var requests = await _context.RescheduleRequests
                .Include(r => r.Booking)
                    .ThenInclude(b => b!.Client)
                .Include(r => r.Booking)
                    .ThenInclude(b => b!.Service)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            return View(requests);
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Approve(int? id)
        {
            if (id == null) return NotFound();

            var request = await _context.RescheduleRequests
                .Include(r => r.Booking)
                    .ThenInclude(b => b!.Client)
                .Include(r => r.Booking)
                    .ThenInclude(b => b!.Service)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (request == null) return NotFound();

            return View(request);
        }

        [HttpPost, ActionName("Approve")]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveConfirmed(int id, string? adminRemarks)
        {
            var request = await _context.RescheduleRequests
                .Include(r => r.Booking)
                    .ThenInclude(b => b!.Service)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (request == null || request.Booking == null) return NotFound();

            bool conflict = await HasBookingConflictExcludingCurrentBooking(
                request.Booking.Id,
                request.RequestedStartTime,
                request.RequestedEndTime);

            if (conflict)
            {
                TempData["Error"] = "Requested schedule conflicts with an existing confirmed booking.";
                return RedirectToAction(nameof(Index));
            }
            var isBlocked = await _context.BlockedDates
                .AnyAsync(x => x.Date.Date == request.RequestedDate.Date);

            if (isBlocked)
            {
                TempData["Error"] = "The requested date is blocked and unavailable.";
                return RedirectToAction(nameof(Index));
            }

            request.Status = RescheduleRequestStatus.Approved;
            request.ReviewedAt = DateTime.Now;
            request.AdminRemarks = adminRemarks;

            request.Booking.EventDate = request.RequestedDate.Date;
            request.Booking.StartTime = request.RequestedStartTime;
            request.Booking.EndTime = request.RequestedEndTime;

            _context.BookingTimelines.Add(new BookingTimeline
            {
                BookingId = request.Booking.Id,
                EventType = TimelineEventType.RescheduleApproved,
                Notes = "Admin approved the reschedule request.",
                CreatedAt = DateTime.Now
            });

            await _context.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(request.Booking.ApplicationUserId))
            {
                await CreateNotificationAsync(
                    request.Booking.ApplicationUserId,
                    "Reschedule Approved",
                    "Your reschedule request was approved.",
                    "/RescheduleRequests/MyRequests");
            }

            var bookingWithClient = await _context.Bookings
                .Include(b => b.Client)
                .FirstOrDefaultAsync(b => b.Id == request.Booking.Id);

            if (bookingWithClient != null && bookingWithClient.Client != null)
            {
                await _emailService.SendEmailAsync(
                    bookingWithClient.Client.Email,
                    "Reschedule Approved",
                    @"
                    <h2>Your Reschedule Request Was Approved</h2>
                    <p>Your booking schedule has been updated successfully.</p>
                    <p>Please log in to view the updated booking details.</p>");
            }

            return RedirectToAction(nameof(Index));
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Reject(int? id)
        {
            if (id == null) return NotFound();

            var request = await _context.RescheduleRequests
                .Include(r => r.Booking)
                    .ThenInclude(b => b!.Client)
                .Include(r => r.Booking)
                    .ThenInclude(b => b!.Service)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (request == null) return NotFound();

            return View(request);
        }

        [HttpPost, ActionName("Reject")]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectConfirmed(int id, string? adminRemarks)
        {
            var request = await _context.RescheduleRequests
                .Include(r => r.Booking)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (request == null || request.Booking == null) return NotFound();

            request.Status = RescheduleRequestStatus.Rejected;
            request.ReviewedAt = DateTime.Now;
            request.AdminRemarks = adminRemarks;

            _context.BookingTimelines.Add(new BookingTimeline
            {
                BookingId = request.Booking.Id,
                EventType = TimelineEventType.RescheduleRejected,
                Notes = "Admin rejected the reschedule request.",
                CreatedAt = DateTime.Now
            });

            await _context.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(request.Booking.ApplicationUserId))
            {
                await CreateNotificationAsync(
                    request.Booking.ApplicationUserId,
                    "Reschedule Rejected",
                    "Your reschedule request was rejected.",
                    "/RescheduleRequests/MyRequests");
            }

            var bookingWithClient = await _context.Bookings
                .Include(b => b.Client)
                .FirstOrDefaultAsync(b => b.Id == request.Booking.Id);

            if (bookingWithClient != null && bookingWithClient.Client != null)
            {
                await _emailService.SendEmailAsync(
                    bookingWithClient.Client.Email,
                    "Reschedule Rejected",
                    $@"
                    <h2>Your Reschedule Request Was Rejected</h2>
                    <p>Your reschedule request was not approved.</p>
                    <p>Remarks: {adminRemarks}</p>");
            }
            TempData["Error"] = "Your reschedule request was rejected. Please review the admin remarks and submit a new request if needed.";
            return RedirectToAction(nameof(MyRequests));
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

            foreach (var booking in confirmedBookings)
            {
                var existingStart = booking.StartTime;
                var existingEndWithBuffer = booking.EndTime.AddHours(1);

                bool overlaps = requestedStart < existingEndWithBuffer && requestedEnd > existingStart;

                if (overlaps)
                {
                    return true;
                }
            }

            return false;
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

        private async Task<List<string>> GetUnavailableRescheduleDatesAsync()
        {
            var blockedDates = await _context.BlockedDates
                .Select(x => x.Date.Date)
                .ToListAsync();

            var settings = await _context.SystemSettings.FirstOrDefaultAsync();
            var defaultMax = settings?.MaxBookingsPerDay ?? 3;

            var customLimits = await _context.DateBookingLimits
                .ToDictionaryAsync(x => x.Date.Date, x => x.MaxBookings);

            var confirmedCounts = await _context.Bookings
                .Where(b => b.Status == BookingStatus.Confirmed)
                .GroupBy(b => b.EventDate.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    Count = g.Count()
                })
                .ToListAsync();

            var fullDates = confirmedCounts
                .Where(x =>
                {
                    var limit = customLimits.ContainsKey(x.Date) ? customLimits[x.Date] : defaultMax;
                    return x.Count >= limit;
                })
                .Select(x => x.Date)
                .ToList();

            return blockedDates
                .Union(fullDates)
                .Select(d => d.ToString("yyyy-MM-dd"))
                .ToList();
        }
        private async Task<bool> HasReachedDailyConfirmedLimitForReschedule(DateTime eventDate)
        {
            var customLimit = await _context.DateBookingLimits
                .Where(x => x.Date.Date == eventDate.Date)
                .Select(x => (int?)x.MaxBookings)
                .FirstOrDefaultAsync();

            var defaultSetting = await _context.SystemSettings.FirstOrDefaultAsync();
            var maxPerDay = customLimit ?? defaultSetting?.MaxBookingsPerDay ?? 3;

            var confirmedCount = await _context.Bookings
                .CountAsync(b => b.Status == BookingStatus.Confirmed
                            && b.EventDate.Date == eventDate.Date);

            return confirmedCount >= maxPerDay;
        }

        [Authorize(Roles = "Client")]
        [HttpGet]
        public async Task<IActionResult> CheckRescheduleDateAvailability(DateTime date)
        {
            var blockedEntry = await _context.BlockedDates
                .FirstOrDefaultAsync(x => x.Date.Date == date.Date);

            var isBlocked = blockedEntry != null;
            var blockReason = blockedEntry?.Reason;

            var customLimit = await _context.DateBookingLimits
                .Where(x => x.Date.Date == date.Date)
                .Select(x => (int?)x.MaxBookings)
                .FirstOrDefaultAsync();

            var settings = await _context.SystemSettings.FirstOrDefaultAsync();
            var maxPerDay = customLimit ?? settings?.MaxBookingsPerDay ?? 3;

            var confirmedCount = await _context.Bookings
                .CountAsync(b => b.Status == BookingStatus.Confirmed && b.EventDate.Date == date.Date);

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