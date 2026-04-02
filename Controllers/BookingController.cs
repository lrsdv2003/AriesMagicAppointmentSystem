using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AriesMagicAppointmentSystem.Controllers
{
    public class BookingsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public BookingsController(ApplicationDbContext context)
        {
            _context = context;
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

        public async Task<IActionResult> Create()
        {
            var viewModel = new BookingCreateViewModel
            {
                EventDate = DateTime.Today,
                Clients = await GetClientListAsync(),
                Services = await GetServiceListAsync()
            };

            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(BookingCreateViewModel model)
        {
            if (!ModelState.IsValid)
            {
                model.Clients = await GetClientListAsync();
                model.Services = await GetServiceListAsync();
                return View(model);
            }

            var selectedService = await _context.Services
            .FirstOrDefaultAsync(s => s.Id == model.ServiceId && !s.IsArchived);

            if (selectedService == null)
            {
                ModelState.AddModelError("", "Selected service is invalid.");
                model.Clients = await GetClientListAsync();
                model.Services = await GetServiceListAsync();
                return View(model);
            }

            var startDateTime = model.EventDate.Date.Add(model.StartTime);
            var endDateTime = startDateTime.AddHours(selectedService.DurationInHours);

            if (await HasReachedDailyConfirmedLimit(model.EventDate))
            {
                ModelState.AddModelError("", "This date already has the maximum of 3 confirmed bookings.");
                model.Clients = await GetClientListAsync();
                model.Services = await GetServiceListAsync();
                return View(model);
            }

            if (await HasBookingConflict(startDateTime, endDateTime))
            {
                ModelState.AddModelError("", "The selected time conflicts with an existing confirmed booking, including the 1-hour buffer.");
                model.Clients = await GetClientListAsync();
                model.Services = await GetServiceListAsync();
                return View(model);
            }

            var booking = new Booking
            {
                ClientId = model.ClientId,
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

            var timelineEntry = new BookingTimeline
            {
                BookingId = booking.Id,
                EventType = TimelineEventType.BookingCreated,
                Notes = "Booking was created by client.",
                CreatedAt = DateTime.Now
            };

            _context.BookingTimelines.Add(timelineEntry);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
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

            return RedirectToAction(nameof(Pending));
        }

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

            return RedirectToAction(nameof(Pending));
        }

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

            return RedirectToAction(nameof(Index));
        }
        private async Task<List<SelectListItem>> GetClientListAsync()
        {
            return await _context.LegacyUsers
                .Where(u => u.Role == "Client")
                .Select(u => new SelectListItem
                {
                    Value = u.Id.ToString(),
                    Text = u.FullName
                })
                .ToListAsync();
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
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddNote(int id, string internalNotes)
        {
            var booking = await _context.Bookings.FindAsync(id);
            if (booking == null) return NotFound();

            booking.InternalNotes = internalNotes;
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }
        /*
          IIIII   L       OOO     V     V   EEEEE    GGGGG    AAAAA    BBBBBB
            I     L      O   O    V     V   E       G        A     A   B     B
            I     L      O   O     V   V    EEEE    G  GGG   AAAAAAA   BBBBBB
            I     L      O   O      V V     E       G    G   A     A   B     B
          IIIII   LLLLL   OOO        V      EEEEE    GGGGG   A     A   BBBBBB
        */
           
    }
}