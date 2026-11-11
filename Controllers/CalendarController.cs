using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AriesMagicAppointmentSystem.Controllers
{
    [Authorize(Roles = "Staff,Owner")]
    public class CalendarController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IHistoryService _historyService;
        private readonly ISystemActivityService _activityService;
        private readonly ILogger<CalendarController>? _logger;

        public CalendarController(ApplicationDbContext context, IHistoryService historyService, ISystemActivityService activityService, ILogger<CalendarController>? logger = null)
        {
            _context = context;
            _historyService = historyService;
            _activityService = activityService;
            _logger = logger;
        }

        /// <summary>
        /// Owner Settings manages blocked dates; fixed capacity remains unchanged.
        /// For Staff/Owner: shows operational calendar with bookings.
        /// </summary>
       [HttpGet]
        public async Task<IActionResult> Index(bool showHistorical = false)
        {
            // Keep the operational calendar honest: anything whose event time has passed moves
            // out of "Confirmed" (and off this calendar) automatically.
            await _historyService.ArchiveDueBookingsAsync();

            var statusesToShow = showHistorical
                ? new[] { BookingStatus.Confirmed, BookingStatus.Completed }
                : new[] { BookingStatus.Confirmed };

            var bookings = await _context.Bookings
                .Include(b => b.Client)
                .Include(b => b.Service)
                .Where(b => statusesToShow.Contains(b.Status))
                .OrderBy(b => b.EventDate)
                .ThenBy(b => b.StartTime)
                .ToListAsync();

            var activePackageNames = await _context.Services
                .AsNoTracking()
                .Where(s => !s.IsArchived)
                .Select(s => s.Name)
                .ToListAsync();

            ViewBag.ShowHistorical = showHistorical;
            ViewBag.PackageColors = activePackageNames
                .Concat(bookings.Select(GetPackageName))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToDictionary(x => x, GetPackageColorKey, StringComparer.OrdinalIgnoreCase);

            var blockedDates = await _context.BlockedDates
                .Select(x => new
                {
                    date = x.Date.ToString("yyyy-MM-dd"),
                    reason = x.Reason
                })
                .ToListAsync();

            var dateLimits = new List<DateBookingLimit>();
            var dateLimitsForCalendar = new Dictionary<string, int>();

            var dailyCounts = bookings
                .GroupBy(b => b.EventDate.Date)
                .ToDictionary(
                    g => g.Key.ToString("yyyy-MM-dd"),
                    g => g.Count()
                );

            var model = new CalendarIndexViewModel
            {
                Bookings = bookings,
                Manage = new CalendarManageViewModel
                {
                    MaxBookingsPerDay = BookingRules.MaximumDailyBookings,
                    BlockedDates = await _context.BlockedDates
                        .OrderByDescending(x => x.Date)
                        .ToListAsync(),
                    DateBookingLimits = dateLimits
                }
            };

            ViewBag.BlockedDates = blockedDates;
            ViewBag.DailyCounts = dailyCounts;
            ViewBag.DateLimits = dateLimitsForCalendar;

            return View("StaffIndex", model);
        }

        private static string GetPackageName(Booking booking) =>
            !string.IsNullOrWhiteSpace(booking.PackageName)
                ? booking.PackageName
                : booking.Service?.Name ?? "Package";

        private static string GetPackageColorKey(string packageName)
        {
            if (packageName.Contains("Premium", StringComparison.OrdinalIgnoreCase)) return "purple";
            if (packageName.Contains("Deluxe", StringComparison.OrdinalIgnoreCase)) return "gold";
            if (packageName.Contains("All In", StringComparison.OrdinalIgnoreCase)) return "blue";
            var stable = packageName.Aggregate(17, (hash, ch) => unchecked(hash * 31 + char.ToUpperInvariant(ch)));
            return new[] { "purple", "gold", "blue", "pink" }[(int)((uint)stable % 4)];
        }

        [HttpGet,Authorize(Roles="Owner")]
        public Task<IActionResult> Settings() => AvailabilitySettingsAsync();

        private async Task<IActionResult> AvailabilitySettingsAsync()
        {
            var model = new CalendarIndexViewModel
            {
                Bookings = new List<Booking>(), // Availability configuration is separate from Staff event coordination
                Manage = new CalendarManageViewModel
                {
                    MaxBookingsPerDay = BookingRules.MaximumDailyBookings,
                    BlockedDates = await _context.BlockedDates
                        .OrderByDescending(x => x.Date)
                        .ToListAsync(),
                    DateBookingLimits = new List<DateBookingLimit>()
                }
            };

            var creatorLogs = await _context.SystemActivities.AsNoTracking()
                .Where(a => a.AffectedRecordType == "BlockedDate" && a.Description.StartsWith("Blocked date"))
                .OrderBy(a => a.CreatedAt).ToListAsync();
            ViewBag.BlockCreators = creatorLogs.GroupBy(a => a.AffectedRecordId ?? "")
                .ToDictionary(g => g.Key, g => g.First().PerformedByUserName ?? "Not recorded");
            return View("Settings",model);
        }

        [Authorize(Roles = "Staff,Owner")]
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
                    contactPerson = b.ContactPerson ?? "",
                    contactNumber = b.ContactNumber ?? "",
                    distanceKm = b.DistanceKm,
                    serviceZone = b.ServiceZone ?? "N/A",
                    eventType = b.EventType ?? "N/A",
                    theme = b.PartyTheme ?? "N/A",
                    packageName = !string.IsNullOrWhiteSpace(b.PackageName) ? b.PackageName : (b.Service != null ? b.Service.Name : "Package"),
                    assignedStaff = b.AssignedStaffName ?? "Not assigned",
                    specialInstructions = "None provided",
                    detailsUrl = Url.Action("Details", "Bookings", new { id = b.Id })
                })
                .ToListAsync();

            return Json(reservations);
        }

        [Authorize(Roles = "Owner")]
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

            if (string.IsNullOrWhiteSpace(model.BlockReason) || model.BlockReason.Length > 255)
            {
                return Json(new
                {
                    success = false,
                    message = "Please provide a reason of up to 255 characters."
                });
            }


            var blockDate = model.BlockDate.Value.Date;

            if (blockDate.Date < DateTime.Today)
            {
                return Json(new
                {
                    success = false,
                    message = "You cannot block a past date."
                });
            }

            await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            if(await _context.Bookings.AnyAsync(b=>b.EventDate.Date==blockDate && b.Status==BookingStatus.Confirmed))
                return Json(new {success=false,message="This date has a confirmed booking. Coordinate an authorized reschedule before blocking it."});

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

                await transaction.CommitAsync();
                return Json(new
                {
                    success = true,
                    message = "Date blocked successfully."
                });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Unable to update blocked date.");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Unable to update calendar availability. Please try again."
                });
            }
        }

        [Authorize(Roles = "Owner")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UnblockDateAjax(int id)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
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

            await transaction.CommitAsync();
            return Json(new { success = true, message = "Blocked date removed successfully." });
        }

    }
}
