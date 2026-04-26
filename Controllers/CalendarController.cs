using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AriesMagicAppointmentSystem.Controllers
{
    [Authorize(Roles = "Staff,Admin")]
    public class CalendarController : Controller
    {
        private readonly ApplicationDbContext _context;

        public CalendarController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var bookings = await _context.Bookings
                .Include(b => b.Client)
                .Include(b => b.Service)
                .Where(b => b.Status == BookingStatus.Confirmed)
                .OrderBy(b => b.EventDate)
                .ThenBy(b => b.StartTime)
                .ToListAsync();

            var blockedDates = await _context.BlockedDates
            .Select(x => new
            {
                date = x.Date.ToString("yyyy-MM-dd"),
                reason = x.Reason
            })
            .ToListAsync();

            var dateLimits = await _context.DateBookingLimits
                .OrderByDescending(x => x.Date)
                .ToListAsync();

            var dateLimitsForCalendar = dateLimits
                .ToDictionary(
                    x => x.Date.ToString("yyyy-MM-dd"),
                    x => x.MaxBookings
                );

            var dailyCounts = bookings
                .GroupBy(b => b.EventDate.Date)
                .ToDictionary(
                    g => g.Key.ToString("yyyy-MM-dd"),
                    g => g.Count()
                );

            var settings = await _context.SystemSettings.FirstOrDefaultAsync();
            if (settings == null)
            {
                settings = new SystemSetting { MaxBookingsPerDay = 3 };
                _context.SystemSettings.Add(settings);
                await _context.SaveChangesAsync();
            }

            var model = new CalendarIndexViewModel
            {
                Bookings = bookings,
                Manage = new CalendarManageViewModel
                {
                    MaxBookingsPerDay = settings.MaxBookingsPerDay,
                    BlockedDates = await _context.BlockedDates
                        .OrderByDescending(x => x.Date)
                        .ToListAsync(),
                    DateBookingLimits = await _context.DateBookingLimits
                        .OrderByDescending(x => x.Date)
                        .ToListAsync()
                }
            };
            ViewBag.DateLimits = dateLimitsForCalendar;
            ViewBag.BlockedDates = blockedDates;
            ViewBag.DailyCounts = dailyCounts;
            ViewBag.MaxBookingsPerDay = settings.MaxBookingsPerDay;

            return View(model);
        }

        [Authorize(Roles = "Staff,Admin")]
        [HttpGet]
        public async Task<IActionResult> GetReservationsByDate(DateTime date)
        {
            var reservations = await _context.Bookings
                .Include(b => b.Client)
                .Include(b => b.Service)
                .Where(b => b.EventDate.Date == date.Date && b.Status == BookingStatus.Confirmed)
                .OrderBy(b => b.StartTime)
                .Select(b => new
                {
                    bookingId = b.Id,
                    bookingCode = "BK-" + b.CreatedAt.Year + "-" + b.Id.ToString("D3"),
                    serviceName = b.Service != null ? b.Service.Name : "N/A",
                    clientName = b.Client != null ? b.Client.FullName : "N/A",
                    eventDate = b.EventDate.ToString("MMMM dd, yyyy"),
                    startTime = b.StartTime.ToString("hh:mm tt"),
                    endTime = b.EndTime.ToString("hh:mm tt"),
                    eventTime = b.StartTime.ToString("hh:mm tt") + " - " + b.EndTime.ToString("hh:mm tt"),
                    venue = b.PartyVenue ?? "N/A",
                    eventType = b.EventType ?? "N/A",
                    theme = b.PartyTheme ?? "N/A",
                    detailsUrl = Url.Action("Details", "Bookings", new { id = b.Id })
                })
                .ToListAsync();

            return Json(reservations);
        }

        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<IActionResult> Manage()
        {
            var settings = await _context.SystemSettings.FirstOrDefaultAsync();

            if (settings == null)
            {
                settings = new SystemSetting { MaxBookingsPerDay = 3 };
                _context.SystemSettings.Add(settings);
                await _context.SaveChangesAsync();
            }

            var blockedDates = await _context.BlockedDates
                .OrderByDescending(x => x.Date)
                .ToListAsync();

            var model = new CalendarManageViewModel
            {
                MaxBookingsPerDay = settings.MaxBookingsPerDay,
                BlockedDates = blockedDates
            };

            return View(model);
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateMaxBookings(CalendarManageViewModel model)
        {
            if (model.MaxBookingsPerDay < 1)
            {
                TempData["Error"] = "Maximum bookings per day must be at least 1.";
                return RedirectToAction(nameof(Manage));
            }

            var settings = await _context.SystemSettings.FirstOrDefaultAsync();

            if (settings == null)
            {
                settings = new SystemSetting();
                _context.SystemSettings.Add(settings);
            }

            settings.MaxBookingsPerDay = model.MaxBookingsPerDay;
            await _context.SaveChangesAsync();

            TempData["Success"] = "Maximum daily bookings updated successfully.";
            return RedirectToAction(nameof(Manage));
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BlockDate(CalendarManageViewModel model)
        {
            if (model.BlockDate == null)
            {
                TempData["Error"] = "Please select a valid date to block.";
                return RedirectToAction(nameof(Manage));
            }

            if (model.BlockDate.Value.Date < DateTime.Today)
            {
                TempData["Error"] = "You cannot block a past date.";
                return RedirectToAction(nameof(Manage));
            }

            var exists = await _context.BlockedDates
                .AnyAsync(x => x.Date.Date == model.BlockDate.Value.Date);

            if (exists)
            {
                TempData["Error"] = "That date is already blocked.";
                return RedirectToAction(nameof(Manage));
            }

            _context.BlockedDates.Add(new BlockedDate
            {
                Date = model.BlockDate.Value.Date,
                Reason = model.BlockReason
            });

            await _context.SaveChangesAsync();

            TempData["Success"] = "Date blocked successfully.";
            return RedirectToAction(nameof(Manage));
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UnblockDate(int id)
        {
            var blocked = await _context.BlockedDates.FindAsync(id);
            if (blocked == null) return NotFound();

            _context.BlockedDates.Remove(blocked);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Blocked date removed successfully.";
            return RedirectToAction(nameof(Manage));
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetDateLimit(CalendarManageViewModel model)
        {
            if (model.LimitDate == null || model.LimitMaxBookings == null)
            {
                TempData["Error"] = "Please select a valid date and maximum booking limit.";
                return RedirectToAction(nameof(Index));
            }

            if (model.LimitDate.Value.Date < DateTime.Today)
            {
                TempData["Error"] = "You cannot set a limit for a past date.";
                return RedirectToAction(nameof(Index));
            }

            if (model.LimitMaxBookings.Value < 0)
            {
                TempData["Error"] = "Maximum bookings cannot be negative.";
                return RedirectToAction(nameof(Index));
            }

            var existing = await _context.DateBookingLimits
                .FirstOrDefaultAsync(x => x.Date.Date == model.LimitDate.Value.Date);

            if (existing == null)
            {
                _context.DateBookingLimits.Add(new DateBookingLimit
                {
                    Date = model.LimitDate.Value.Date,
                    MaxBookings = model.LimitMaxBookings.Value
                });
            }
            else
            {
                existing.MaxBookings = model.LimitMaxBookings.Value;
            }

            await _context.SaveChangesAsync();

            TempData["Success"] = "Date-specific booking limit saved successfully.";
            return RedirectToAction(nameof(Index));
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveDateLimit(int id)
        {
            var item = await _context.DateBookingLimits.FindAsync(id);
            if (item == null) return NotFound();

            _context.DateBookingLimits.Remove(item);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Date-specific booking limit removed successfully.";
            return RedirectToAction(nameof(Index));
        }
    }
}