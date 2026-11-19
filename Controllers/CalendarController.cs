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
        /// Owner Settings manages blocked dates and validated date-specific capacity;
        /// Staff/Owner Index shows operational confirmed bookings.
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
                .ThenBy(b => b.Id)
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

            var dateLimits = await _context.DateBookingLimits.AsNoTracking()
                .OrderBy(x => x.Date).ThenBy(x => x.Id).ToListAsync();
            var dateLimitsForCalendar = DateBookingCapacity.Overrides(dateLimits)
                .ToDictionary(x => x.Key.ToString("yyyy-MM-dd"), x => x.Value);

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
                    DateBookingLimits = await _context.DateBookingLimits.AsNoTracking()
                        .OrderBy(x => x.Date).ThenBy(x => x.Id).ToListAsync()
                }
            };

            ViewBag.ConfirmedByDate = await _context.Bookings.AsNoTracking()
                .Where(x => x.Status == BookingStatus.Confirmed)
                .GroupBy(x => x.EventDate.Date)
                .Select(x => new { Date = x.Key, Count = x.Count() })
                .ToDictionaryAsync(x => x.Date, x => x.Count);
            return View("Settings",model);
        }

        // A date may have one configured override. Legacy duplicate rows are
        // reported rather than silently overwritten or deleted.
        [Authorize(Roles = "Owner")]
        [HttpGet]
        public async Task<IActionResult> GetDateLimit(DateTime date)
        {
            if (!ModelState.IsValid || date == default || date.Date < DateTime.Today || date.Date == DateTime.MaxValue.Date)
                return BadRequest(new { success = false, message = "Select today or a future date." });

            var existing = await _context.DateBookingLimits.AsNoTracking()
                .Where(x => x.Date.Date == date.Date)
                .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
                .Take(2).ToListAsync();
            if (existing.Count > 1)
                return Conflict(new { success = false, message = "Multiple limits exist for this date. Resolve the duplicate records before editing." });

            var confirmed = await _context.Bookings.AsNoTracking()
                .CountAsync(x => x.Status == BookingStatus.Confirmed && x.EventDate.Date == date.Date);
            var isBlocked = await _context.BlockedDates.AsNoTracking()
                .AnyAsync(x => x.Date.Date == date.Date);
            var configured = existing.FirstOrDefault();
            return Json(new {
                success = true,
                date = date.ToString("yyyy-MM-dd"),
                id = configured?.Id ?? 0,
                storedLimit = configured?.MaxBookings ?? 0,
                effectiveLimit = configured != null && DateBookingCapacity.IsValid(configured.MaxBookings)
                    ? configured.MaxBookings : BookingRules.MaximumDailyBookings,
                confirmedCount = confirmed,
                isBlocked
            });
        }

        [Authorize(Roles = "Owner")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveDateLimit(DateTime limitDate, int recordId, int expectedStoredLimit, int newLimit)
        {
            if (!ModelState.IsValid || limitDate == default || limitDate.Date < DateTime.Today || limitDate.Date == DateTime.MaxValue.Date)
                return BadRequest(new { success = false, message = "Select a valid current or future date." });
            if (!DateBookingCapacity.IsValid(newLimit))
                return BadRequest(new { success = false,
                    message = $"Enter a whole number from {DateBookingCapacity.MinimumLimit} to {DateBookingCapacity.MaximumLimit}. Block dates separately to close availability." });
            if (recordId < 0)
                return BadRequest(new { success = false, message = "Invalid date setting. Reload and try again." });

            try
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
                var start = limitDate.Date;
                var next = start.AddDays(1);
                // SQL Server update/range locks serialize simultaneous changes for the same date,
                // including two concurrent attempts to create its first override.
                var entries = await _context.DateBookingLimits
                    .FromSqlInterpolated($"SELECT * FROM [DateBookingLimits] WITH (UPDLOCK, HOLDLOCK) WHERE [Date] >= {start} AND [Date] < {next}")
                    .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
                    .Take(2).ToListAsync();

                if (entries.Count > 1)
                    return Conflict(new { success = false, message = "Multiple limits exist for this date. No change was made." });

                var current = entries.FirstOrDefault();
                if ((current?.Id ?? 0) != recordId || (current?.MaxBookings ?? 0) != expectedStoredLimit)
                    return Conflict(new { success = false,
                        message = "This date was changed by another request. Reload its latest limit before saving." });

                var occupied = await _context.Bookings.AsNoTracking()
                    .CountAsync(x => x.Status == BookingStatus.Confirmed && x.EventDate.Date == start);
                if (newLimit < occupied)
                    return Conflict(new { success = false,
                        message = $"This date already has {occupied} confirmed booking(s). The daily limit cannot be lower." });

                if (current == null)
                {
                    current = new DateBookingLimit { Date = start, MaxBookings = newLimit };
                    _context.DateBookingLimits.Add(current);
                }
                else
                {
                    current.MaxBookings = newLimit;
                }

                await _context.SaveChangesAsync();
                await _activityService.LogAsync(
                    SystemActivityType.CalendarModified,
                    $"Daily booking limit for {start:MMMM dd, yyyy}: {(recordId == 0 ? BookingRules.MaximumDailyBookings : expectedStoredLimit)} to {newLimit} confirmed bookings",
                    User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "Unknown",
                    User.Identity?.Name ?? "Unknown",
                    current.Id.ToString(),
                    "DateBookingLimit");
                await transaction.CommitAsync();
                return Json(new { success = true, message = $"Daily booking limit for {start:MMM d, yyyy} saved as {newLimit}.", newLimit });
            }
            catch (Exception ex) when (ex is DbUpdateException || ex is Microsoft.Data.SqlClient.SqlException)
            {
                _logger?.LogWarning(ex, "Concurrent Calendar Settings limit update was rejected.");
                return Conflict(new { success = false, message = "The date setting changed or is temporarily unavailable. Reload and retry." });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Calendar Settings daily-limit update failed.");
                return StatusCode(500, new { success = false, message = "Unable to save this date's booking limit. Please try again." });
            }
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
