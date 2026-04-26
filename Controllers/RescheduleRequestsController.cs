using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

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

            return View(viewModel);
        }

        [HttpPost]
        [Authorize(Roles = "Client")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(RescheduleRequestCreateViewModel model)
        {
            var appUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (model.RequestedDate.Date < DateTime.Today)
            {
                ModelState.AddModelError("RequestedDate", "Past dates are not allowed.");
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
    }
}