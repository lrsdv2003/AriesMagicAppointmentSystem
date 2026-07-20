using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AriesMagicAppointmentSystem.Controllers
{
    [Authorize(Roles = "Staff,Admin,Owner")]
    public class CalendarController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IHistoryService _historyService;
        private readonly ISystemActivityService _activityService;

        public CalendarController(ApplicationDbContext context, IHistoryService historyService, ISystemActivityService activityService)
        {
            _context = context;
            _historyService = historyService;
            _activityService = activityService;
        }

        /// <summary>
        /// For Admin: shows calendar administration tools (blocked dates, limits).
        /// For Staff/Owner: shows operational calendar with bookings.
        /// </summary>
        public async Task<IActionResult> Index(bool showHistorical = false)
        {
            if (User.IsInRole("Admin"))
            {
                return await AdminIndexAsync();
            }

            // Keep the operational calendar honest: anything whose event time has passed moves
            // out of "Confirmed" (and off this calendar) automatically.
            await _historyService.ArchiveDueBookingsAsync();

            var statusesToShow = showHistorical
                ? new[] { BookingStatus.Confirmed, BookingStatus.Completed }
                : new[] { BookingStatus.Confirmed };

            var bookings = await _context.Bookings
                .Include(b => b.Client)
                .Include(b => b.Service)
                .Include(b => b.Payments)
                .Where(b => statusesToShow.Contains(b.Status))
                .OrderBy(b => b.EventDate)
                .ThenBy(b => b.StartTime)
                .ToListAsync();

            ViewBag.ShowHistorical = showHistorical;

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

        private async Task<IActionResult> AdminIndexAsync()
        {
            var settings = await _context.SystemSettings.FirstOrDefaultAsync();
            if (settings == null)
            {
                settings = new SystemSetting { MaxBookingsPerDay = 3 };
                _context.SystemSettings.Add(settings);
                await _context.SaveChangesAsync();
            }

            var model = new CalendarIndexViewModel
            {
                Bookings = new List<Booking>(), // Admin doesn't see operational bookings
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

            return View(model);
        }

        [Authorize(Roles = "Staff,Admin,Owner")]
        [HttpGet]
        public async Task<IActionResult> GetReservationsByDate(DateTime date, bool showHistorical = false)
        {
            var statusesToShow = showHistorical
                ? new[] { BookingStatus.Confirmed, BookingStatus.Completed }
                : new[] { BookingStatus.Confirmed };

            var reservations = await _context.Bookings
                .Include(b => b.Client)
                .Include(b => b.Service)
                .Where(b => b.EventDate.Date == date.Date && statusesToShow.Contains(b.Status))
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
        public async Task<IActionResult> UpdateMaxBookingsAjax(
            [FromForm] CalendarManageViewModel model)
        {
            if (model.MaxBookingsPerDay < 1)
            {
                return Json(new
                {
                    success = false,
                    message = "Maximum bookings per day must be at least 1."
                });
            }

            try
            {
                var settings = await _context.SystemSettings.FirstOrDefaultAsync();

                if (settings == null)
                {
                    settings = new SystemSetting
                    {
                        MaxBookingsPerDay = model.MaxBookingsPerDay
                    };

                    _context.SystemSettings.Add(settings);
                }

                var oldMax = settings.MaxBookingsPerDay;
                settings.MaxBookingsPerDay = model.MaxBookingsPerDay;

                await _context.SaveChangesAsync();

                await _activityService.LogAsync(
                    SystemActivityType.SettingsChanged,
                    $"Updated default daily booking limit from {oldMax} to {model.MaxBookingsPerDay}",
                    User.FindFirst(
                        System.Security.Claims.ClaimTypes.NameIdentifier
                    )?.Value ?? "Unknown",
                    User.Identity?.Name ?? "Unknown",
                    "SystemSettings",
                    "SystemSettings",
                    new
                    {
                        oldMax,
                        newMax = model.MaxBookingsPerDay
                    });

                return Json(new
                {
                    success = true,
                    message = "Maximum daily bookings updated successfully."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.InnerException?.Message ?? ex.Message
                });
            }
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

            // Treat the date as local (not UTC) to avoid timezone shift
            var limitDate = DateTime.SpecifyKind(model.LimitDate.Value.Date, DateTimeKind.Local);

            if (limitDate.Date < DateTime.Today)
            {
                return Json(new { success = false, message = "You cannot set a limit for a past date." });
            }

            if (model.LimitMaxBookings.Value < 0)
            {
                return Json(new { success = false, message = "Maximum bookings cannot be negative." });
            }

            var existing = await _context.DateBookingLimits
                .FirstOrDefaultAsync(x => x.Date.Date == limitDate.Date);

            var isNew = existing == null;
            int activityRecordId;

            if (existing == null)
            {
                var newLimit = new DateBookingLimit
                {
                    Date = limitDate,
                    MaxBookings = model.LimitMaxBookings.Value
                };

                _context.DateBookingLimits.Add(newLimit);
                await _context.SaveChangesAsync();
                activityRecordId = newLimit.Id;
            }
            else
            {
                existing.MaxBookings = model.LimitMaxBookings.Value;
                await _context.SaveChangesAsync();
                activityRecordId = existing.Id;
            }

            await _activityService.LogAsync(
                SystemActivityType.CalendarModified,
                isNew
                    ? $"Set custom daily limit of {model.LimitMaxBookings} for {limitDate:MMMM dd, yyyy}"
                    : $"Updated custom daily limit to {model.LimitMaxBookings} for {limitDate:MMMM dd, yyyy}",
                User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "Unknown",
                User.Identity?.Name ?? "Unknown",
                activityRecordId.ToString(),
                "DateBookingLimit",
                new { date = limitDate, maxBookings = model.LimitMaxBookings.Value, isNew }
            );

            return Json(new { success = true, message = "Date-specific booking limit saved successfully." });
        }
        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BlockDateAjax(
            [FromForm] CalendarManageViewModel model)
        {
            if (model.BlockDate == null)
            {
                return Json(new
                {
                    success = false,
                    message = "Please select a valid date to block."
                });
            }

            if (string.IsNullOrWhiteSpace(model.BlockReason))
            {
                return Json(new
                {
                    success = false,
                    message = "Please provide a reason."
                });
            }

            var blockDate = DateTime.SpecifyKind(
                model.BlockDate.Value.Date,
                DateTimeKind.Local
            );

            if (blockDate.Date < DateTime.Today)
            {
                return Json(new
                {
                    success = false,
                    message = "You cannot block a past date."
                });
            }

            var exists = await _context.BlockedDates
                .AnyAsync(x => x.Date.Date == blockDate.Date);

            if (exists)
            {
                return Json(new
                {
                    success = false,
                    message = "That date is already blocked."
                });
            }

            try
            {
                var blockedDate = new BlockedDate
                {
                    Date = blockDate,
                    Reason = model.BlockReason.Trim()
                };

                _context.BlockedDates.Add(blockedDate);
                await _context.SaveChangesAsync();

                await _activityService.LogAsync(
                    SystemActivityType.CalendarModified,
                    $"Blocked date {blockDate:MMMM dd, yyyy} - {model.BlockReason.Trim()}",
                    User.FindFirst(
                        System.Security.Claims.ClaimTypes.NameIdentifier
                    )?.Value ?? "Unknown",
                    User.Identity?.Name ?? "Unknown",
                    blockedDate.Id.ToString(),
                    "BlockedDate"
                );

                return Json(new
                {
                    success = true,
                    message = "Date blocked successfully."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = ex.InnerException?.Message ?? ex.Message
                });
            }
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

            var reason = blocked.Reason;
            var date = blocked.Date;

            _context.BlockedDates.Remove(blocked);
            await _context.SaveChangesAsync();

            await _activityService.LogAsync(
                SystemActivityType.CalendarModified,
                $"Unblocked date {date:MMMM dd, yyyy} (was: {reason})",
                User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "Unknown",
                User.Identity?.Name ?? "Unknown",
                id.ToString(),
                "BlockedDate"
            );

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

            var limitDate = item.Date;
            var maxBookings = item.MaxBookings;

            _context.DateBookingLimits.Remove(item);
            await _context.SaveChangesAsync();

            await _activityService.LogAsync(
                SystemActivityType.CalendarModified,
                $"Removed custom daily limit of {maxBookings} for {limitDate:MMMM dd, yyyy}",
                User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "Unknown",
                User.Identity?.Name ?? "Unknown",
                id.ToString(),
                "DateBookingLimit"
            );

            return Json(new { success = true, message = "Date-specific booking limit removed successfully." });
        }
    }
}