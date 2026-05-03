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

            var dateLimitsForCalendar = dateLimits.ToDictionary(
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
                    DateBookingLimits = dateLimits
                }
            };

            ViewBag.BlockedDates = blockedDates;
            ViewBag.DailyCounts = dailyCounts;
            ViewBag.DateLimits = dateLimitsForCalendar;

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
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateMaxBookingsAjax([FromForm] CalendarManageViewModel model)
        {
            if (model.MaxBookingsPerDay < 1)
            {
                return Json(new { success = false, message = "Maximum bookings per day must be at least 1." });
            }

            var settings = await _context.SystemSettings.FirstOrDefaultAsync();

            if (settings == null)
            {
                settings = new SystemSetting();
                _context.SystemSettings.Add(settings);
            }

            settings.MaxBookingsPerDay = model.MaxBookingsPerDay;
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Maximum daily bookings updated successfully." });
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BlockDateAjax([FromForm] CalendarManageViewModel model)
        {
            if (model.BlockDate == null)
            {
                return Json(new { success = false, message = "Please select a valid date to block." });
            }

            if (model.BlockDate.Value.Date < DateTime.Today)
            {
                return Json(new { success = false, message = "You cannot block a past date." });
            }

            var exists = await _context.BlockedDates
                .AnyAsync(x => x.Date.Date == model.BlockDate.Value.Date);

            if (exists)
            {
                return Json(new { success = false, message = "That date is already blocked." });
            }

            _context.BlockedDates.Add(new BlockedDate
            {
                Date = model.BlockDate.Value.Date,
                Reason = model.BlockReason
            });

            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Date blocked successfully." });
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetDateLimitAjax([FromForm] CalendarManageViewModel model)
        {
            if (model.LimitDate == null || model.LimitMaxBookings == null)
            {
                return Json(new { success = false, message = "Please select a valid date and maximum booking limit." });
            }

            if (model.LimitDate.Value.Date < DateTime.Today)
            {
                return Json(new { success = false, message = "You cannot set a limit for a past date." });
            }

            if (model.LimitMaxBookings.Value < 0)
            {
                return Json(new { success = false, message = "Maximum bookings cannot be negative." });
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

            return Json(new { success = true, message = "Date-specific booking limit saved successfully." });
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UnblockDateAjax(int id)
        {
            var blocked = await _context.BlockedDates.FindAsync(id);
            if (blocked == null)
            {
                return Json(new { success = false, message = "Blocked date not found." });
            }

            _context.BlockedDates.Remove(blocked);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Blocked date removed successfully." });
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveDateLimitAjax(int id)
        {
            var item = await _context.DateBookingLimits.FindAsync(id);
            if (item == null)
            {
                return Json(new { success = false, message = "Date-specific booking limit not found." });
            }

            _context.DateBookingLimits.Remove(item);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Date-specific booking limit removed successfully." });
        }
    }
}