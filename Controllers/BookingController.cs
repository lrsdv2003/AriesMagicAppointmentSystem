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
    public class BookingsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailService _emailService;

        public BookingsController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IEmailService emailService)
        {
            _context = context;
            _userManager = userManager;
            _emailService = emailService;
        }

        public async Task<IActionResult> Index()
        {
            var bookings = await _context.Bookings
                .Include(b => b.Client)
                .Include(b => b.Service)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();

            return View(bookings);
        }

        [Authorize(Roles = "Client")]
        public async Task<IActionResult> MyBookings()
        {
            var appUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var bookings = await _context.Bookings
                .Include(b => b.Client)
                .Include(b => b.Service)
                .Where(b => b.ApplicationUserId == appUserId)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();

            return View(bookings);
        }

        [Authorize(Roles = "Client")]
        public async Task<IActionResult> Create()
        {
            var viewModel = new BookingCreateViewModel
            {
                EventDate = DateTime.Today,
                Services = await GetServiceListAsync()
            };

            return View(viewModel);
        }

        [HttpPost]
        [Authorize(Roles = "Client")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(BookingCreateViewModel model)
        {
            if (!ModelState.IsValid)
            {
                model.Services = await GetServiceListAsync();
                return View(model);
            }

            var selectedService = await _context.Services
                .FirstOrDefaultAsync(s => s.Id == model.ServiceId && !s.IsArchived);

            if (selectedService == null)
            {
                ModelState.AddModelError("", "Selected service is invalid.");
                model.Services = await GetServiceListAsync();
                return View(model);
            }

            var appUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var appUser = await _userManager.GetUserAsync(User);

            if (string.IsNullOrEmpty(appUserId) || appUser == null)
            {
                return Challenge();
            }

            var legacyClient = await _context.LegacyUsers
                .FirstOrDefaultAsync(u => u.Email == appUser.Email);

            if (legacyClient == null)
            {
                legacyClient = new User
                {
                    FullName = appUser.FullName,
                    Email = appUser.Email!,
                    PasswordHash = "IDENTITY_MANAGED",
                    Role = "Client"
                };

                _context.LegacyUsers.Add(legacyClient);
                await _context.SaveChangesAsync();
            }

            var startDateTime = model.EventDate.Date.Add(model.StartTime);
            var endDateTime = startDateTime.AddHours(selectedService.DurationInHours);

            if (await HasReachedDailyConfirmedLimit(model.EventDate))
            {
                ModelState.AddModelError("", "This date already has the maximum of 3 confirmed bookings.");
                model.Services = await GetServiceListAsync();
                return View(model);
            }

            if (await HasBookingConflict(startDateTime, endDateTime))
            {
                ModelState.AddModelError("", "The selected time conflicts with an existing confirmed booking, including the 1-hour buffer.");
                model.Services = await GetServiceListAsync();
                return View(model);
            }

            var booking = new Booking
            {
                ClientId = legacyClient.Id,
                ApplicationUserId = appUserId,
                ServiceId = model.ServiceId,
                EventDate = model.EventDate.Date,
                StartTime = startDateTime,
                EndTime = endDateTime,
                Status = BookingStatus.Pending,
                CreatedAt = DateTime.Now,
                IsCompletedLocked = false
            };

            _context.Bookings.Add(booking);
            await _context.SaveChangesAsync();

            _context.BookingTimelines.Add(new BookingTimeline
            {
                BookingId = booking.Id,
                EventType = TimelineEventType.BookingCreated,
                Notes = "Booking was created by client.",
                CreatedAt = DateTime.Now
            });

            await _context.SaveChangesAsync();

            var staffUsers = await _context.LegacyUsers
                .Where(u => u.Role == "Staff")
                .ToListAsync();

            foreach (var staff in staffUsers)
            {
                await CreateNotificationAsync(
                    staff.Id,
                    "New Booking Submitted",
                    $"A new booking was submitted by {legacyClient.FullName}.",
                    "/Bookings/Pending");
            }
            var staffEmails = await _context.LegacyUsers
                .Where(u => u.Role == "Staff")
                .Select(u => u.Email)
                .ToListAsync();

            foreach (var email in staffEmails)
            {
                await _emailService.SendEmailAsync(
                    email,
                    "New Booking Submitted",
                    $@"
                    <h2>New Booking Submitted</h2>
                    <p>A new booking was submitted by <strong>{legacyClient.FullName}</strong>.</p>
                    <p>Service: <strong>{selectedService.Name}</strong></p>
                    <p>Date: <strong>{model.EventDate:MMMM dd, yyyy}</strong></p>
                    <p>Please review it in the staff dashboard.</p>");
            }

            return RedirectToAction(nameof(MyBookings));
        }

        [Authorize(Roles = "Staff")]
        public async Task<IActionResult> Pending()
        {
            var pendingBookings = await _context.Bookings
                .Include(b => b.Client)
                .Include(b => b.Service)
                .Where(b => b.Status == BookingStatus.Pending)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();

            return View(pendingBookings);
        }

        [Authorize(Roles = "Staff")]
        public async Task<IActionResult> Approve(int? id)
        {
            if (id == null) return NotFound();

            var booking = await _context.Bookings
                .Include(b => b.Client)
                .Include(b => b.Service)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (booking == null) return NotFound();

            return View(booking);
        }

        [HttpPost, ActionName("Approve")]
        [Authorize(Roles = "Staff")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveConfirmed(int id)
        {
            var booking = await _context.Bookings.FindAsync(id);
            if (booking == null) return NotFound();

            booking.Status = BookingStatus.AwaitingDownpayment;
            await _context.SaveChangesAsync();

            _context.BookingTimelines.Add(new BookingTimeline
            {
                BookingId = booking.Id,
                EventType = TimelineEventType.BookingApproved,
                Notes = "Booking approved by staff and is now awaiting downpayment.",
                CreatedAt = DateTime.Now
            });

            await _context.SaveChangesAsync();

            await CreateNotificationAsync(
                booking.ClientId,
                "Booking Approved",
                "Your booking has been approved and is now awaiting downpayment.",
                "/Bookings/MyBookings");

            var bookingWithClient = await _context.Bookings
                .Include(b => b.Client)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (bookingWithClient != null && bookingWithClient.Client != null)
            {
                await _emailService.SendEmailAsync(
                    bookingWithClient.Client.Email,
                    "Booking Approved",
                    @"
                    <h2>Your Booking Was Approved</h2>
                    <p>Your booking has been approved and is now awaiting downpayment.</p>
                    <p>Please log in to the system and upload your payment proof.</p>");
            }

            return RedirectToAction(nameof(Pending));
        }

        [Authorize(Roles = "Staff")]
        public async Task<IActionResult> Decline(int? id)
        {
            if (id == null) return NotFound();

            var booking = await _context.Bookings
                .Include(b => b.Client)
                .Include(b => b.Service)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (booking == null) return NotFound();

            return View(booking);
        }

        [HttpPost, ActionName("Decline")]
        [Authorize(Roles = "Staff")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeclineConfirmed(int id)
        {
            var booking = await _context.Bookings.FindAsync(id);
            if (booking == null) return NotFound();

            booking.Status = BookingStatus.Declined;
            await _context.SaveChangesAsync();

            _context.BookingTimelines.Add(new BookingTimeline
            {
                BookingId = booking.Id,
                EventType = TimelineEventType.BookingDeclined,
                Notes = "Booking declined by staff.",
                CreatedAt = DateTime.Now
            });

            await _context.SaveChangesAsync();

            await CreateNotificationAsync(
                booking.ClientId,
                "Booking Declined",
                "Your booking request was declined.",
                "/Bookings/MyBookings");

            var bookingWithClient = await _context.Bookings
                .Include(b => b.Client)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (bookingWithClient != null && bookingWithClient.Client != null)
            {
                await _emailService.SendEmailAsync(
                    bookingWithClient.Client.Email,
                    "Booking Declined",
                    @"
                    <h2>Your Booking Was Declined</h2>
                    <p>We’re sorry, but your booking request was declined.</p>
                    <p>Please contact the team or create a new booking request if needed.</p>");
            }

            return RedirectToAction(nameof(Pending));
        }

        [Authorize(Roles = "Staff")]
        public async Task<IActionResult> Complete(int? id)
        {
            if (id == null) return NotFound();

            var booking = await _context.Bookings
                .Include(b => b.Client)
                .Include(b => b.Service)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (booking == null) return NotFound();

            return View(booking);
        }

        [HttpPost, ActionName("Complete")]
        [Authorize(Roles = "Staff")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CompleteConfirmed(int id)
        {
            var booking = await _context.Bookings.FindAsync(id);
            if (booking == null) return NotFound();

            booking.Status = BookingStatus.Completed;
            booking.IsCompletedLocked = true;
            await _context.SaveChangesAsync();

            _context.BookingTimelines.Add(new BookingTimeline
            {
                BookingId = booking.Id,
                EventType = TimelineEventType.BookingCompleted,
                Notes = "Booking marked as completed by staff.",
                CreatedAt = DateTime.Now
            });

            await _context.SaveChangesAsync();

            await CreateNotificationAsync(
                booking.ClientId,
                "Booking Completed",
                "Your booking has been marked as completed.",
                "/Bookings/MyBookings");

            var bookingWithClient = await _context.Bookings
                .Include(b => b.Client)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (bookingWithClient != null && bookingWithClient.Client != null)
            {
                await _emailService.SendEmailAsync(
                    bookingWithClient.Client.Email,
                    "Booking Completed",
                    @"
                    <h2>Your Booking Was Completed</h2>
                    <p>Your booking has been marked as completed.</p>
                    <p>Thank you for choosing Aries Magic.</p>");
            }

            return RedirectToAction(nameof(Index));
        }

        [Authorize(Roles = "Staff")]
        public async Task<IActionResult> AddNote(int? id)
        {
            if (id == null) return NotFound();

            var booking = await _context.Bookings
                .Include(b => b.Client)
                .Include(b => b.Service)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (booking == null) return NotFound();

            return View(booking);
        }

        [HttpPost]
        [Authorize(Roles = "Staff")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddNote(int id, string internalNotes)
        {
            var booking = await _context.Bookings.FindAsync(id);
            if (booking == null) return NotFound();

            booking.InternalNotes = internalNotes;
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        private async Task<List<SelectListItem>> GetServiceListAsync()
        {
            return await _context.Services
                .Where(s => !s.IsArchived)
                .Select(s => new SelectListItem
                {
                    Value = s.Id.ToString(),
                    Text = s.Name
                })
                .ToListAsync();
        }

        private async Task<bool> HasBookingConflict(DateTime requestedStart, DateTime requestedEnd)
        {
            var confirmedBookings = await _context.Bookings
                .Where(b => b.Status == BookingStatus.Confirmed)
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

        private async Task<bool> HasReachedDailyConfirmedLimit(DateTime eventDate)
        {
            var confirmedCount = await _context.Bookings
                .CountAsync(b => b.Status == BookingStatus.Confirmed
                              && b.EventDate.Date == eventDate.Date);

            return confirmedCount >= 3;
        }
        private async Task CreateNotificationAsync(int userId, string title, string message, string? link = null)
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