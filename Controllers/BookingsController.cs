using System.Security.Claims;
using System.Text.Json;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AriesMagicAppointmentSystem.Controllers
{
    public class BookingsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IHistoryService _historyService;
        private readonly IVenueDistanceService _venueDistanceService;
        private readonly IGeocodingService _geocodingService;
        private readonly IPaymentFinancialService _financialService;
        private readonly ISystemActivityService _activityService;
        private const int MaxRemovedInclusions = 2;
        private const decimal FixedInclusionDeduction = 2000m;
        private const decimal FixedRequiredDownpayment = 2000m;

        public BookingsController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IHistoryService historyService,
            IVenueDistanceService venueDistanceService,
            IGeocodingService geocodingService,
            IPaymentFinancialService financialService,
            ISystemActivityService activityService)
        {
            _context = context;
            _userManager = userManager;
            _historyService = historyService;
            _venueDistanceService = venueDistanceService;
            _geocodingService = geocodingService;
            _financialService = financialService;
            _activityService = activityService;
        }

        [Authorize(Roles = "Staff,Owner")]
        public IActionResult Pending()
        {
            return RedirectToAction(nameof(Index), new { bookingStatus = "Pending" });
        }

        [Authorize(Roles = "Staff,Owner")]
        public async Task<IActionResult> Index(string? search, string? bookingStatus, string? paymentStatus, DateTime? eventDate, int? serviceId)
        {
            // Lazily flip any bookings whose event has already ended into Completed/archived
            // before we build this list, so nothing that has already happened lingers here.
            await _historyService.ArchiveDueBookingsAsync();
            var isStaff = User.IsInRole("Staff");

            var bookingsQuery = _context.Bookings
                .Include(b => b.Client)
                .Include(b => b.Service)
                .Where(b => b.Status != BookingStatus.Completed)
                .AsQueryable();

            if (isStaff)
            {
                bookingsQuery = bookingsQuery.Where(b =>
                    b.Status == BookingStatus.Pending ||
                    b.Status == BookingStatus.AwaitingDownpayment ||
                    b.Status == BookingStatus.AwaitingVerification ||
                    b.Status == BookingStatus.Declined);

                bookingStatus = string.IsNullOrWhiteSpace(bookingStatus) ? "AllRequests" : bookingStatus;
                bookingsQuery = bookingStatus switch
                {
                    "Pending" => bookingsQuery.Where(b => b.Status == BookingStatus.Pending),
                    "ApprovedAwaitingPayment" => bookingsQuery.Where(b => b.Status == BookingStatus.AwaitingDownpayment || b.Status == BookingStatus.AwaitingVerification),
                    "Declined" => bookingsQuery.Where(b => b.Status == BookingStatus.Declined),
                    "RequiresReview" => bookingsQuery.Where(b => b.RequiresManualReview),
                    _ => bookingsQuery
                };
            }
            else if (!string.IsNullOrWhiteSpace(bookingStatus) && bookingStatus != "All")
            {
                bookingsQuery = bookingsQuery.Where(b => b.Status == bookingStatus);
            }

            if (eventDate.HasValue)
            {
                bookingsQuery = bookingsQuery.Where(b => b.EventDate.Date == eventDate.Value.Date);
            }

            if (serviceId.HasValue)
            {
                bookingsQuery = bookingsQuery.Where(b => b.ServiceId == serviceId.Value);
            }

            var bookings = await bookingsQuery
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var lowered = search.ToLower();

                bookings = bookings.Where(b =>
                    $"BK-{b.CreatedAt.Year}-{b.Id:D3}".ToLower().Contains(lowered) ||
                    (b.Client?.FullName?.ToLower().Contains(lowered) ?? false) ||
                    (b.Client?.Email?.ToLower().Contains(lowered) ?? false) ||
                    (b.Service?.Name?.ToLower().Contains(lowered) ?? false)
                ).ToList();
            }

            var bookingFinancials = await _financialService.GetSummariesAsync(bookings.Select(b => b.Id));
            var rows = bookings.Select(b =>
            {
                var latestPaymentStatus = bookingFinancials.TryGetValue(b.Id, out var financial)
                    ? financial.PaymentStatus
                    : BookingPaymentState.Unpaid;

                return new BookingManagementRowViewModel
                {
                    Id = b.Id,
                    BookingCode = $"BK-{b.CreatedAt.Year}-{b.Id:D3}",
                    ClientName = b.Client?.FullName ?? "N/A",
                    ServiceName = b.Service?.Name ?? "N/A",
                    EventDate = b.EventDate,
                    StartTime = b.StartTime,
                    EndTime = b.EndTime,
                    VenueAddress = b.PartyVenue,
                    IsServiceable = b.IsServiceable,
                    BookingStatus = b.Status,
                    PaymentStatus = latestPaymentStatus,
                    InternalNotes = b.InternalNotes,
                    DistanceKm = b.DistanceKm,
                    ServiceZone = b.ServiceZone,
                    TravelFee = b.TravelFee,
                    RequiresManualReview = b.RequiresManualReview
                };
            }).ToList();

            if (!isStaff && !string.IsNullOrWhiteSpace(paymentStatus) && paymentStatus != "All")
            {
                rows = rows.Where(r => r.PaymentStatus == paymentStatus).ToList();
            }

            var viewModel = new BookingManagementViewModel
            {
                Search = search,
                BookingStatus = bookingStatus ?? "All",
                PaymentStatus = paymentStatus ?? "All",
                EventDate = eventDate,
                ServiceId = serviceId,
                AvailableServices = await _context.Services
                    .AsNoTracking()
                    .Where(s => !s.IsArchived)
                    .OrderBy(s => s.Name)
                    .ToListAsync(),
                Bookings = rows
            };

            return isStaff ? View("StaffIndex", viewModel) : View(viewModel);
        }

        [Authorize(Roles = "Client")]
        [HttpGet]
        public async Task<IActionResult> SearchVenue(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return Json(Array.Empty<object>());

            try
            {
                var results = await _geocodingService.SearchAsync(query);
                return Json(results.Select(r => new
                {
                    displayName = r.DisplayName,
                    latitude = r.Latitude,
                    longitude = r.Longitude
                }));
            }
            catch
            {
                return StatusCode(503, new { message = "Venue search is temporarily unavailable." });
            }
        }

        [Authorize(Roles = "Client")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CheckVenueServiceability(string partyVenue, DateTime? eventDate, TimeSpan? startTime)
        {
            if (string.IsNullOrWhiteSpace(partyVenue))
            {
                return BadRequest(new
                {
                    success = false,
                    message = "Please select a venue before continuing."
                });
            }

            try
            {
                // Resolve the venue again on the server. Client-posted coordinates,
                // distance, travel fee, and serviceability are never trusted.
                var matches = await _geocodingService.SearchAsync(partyVenue);
                var selected = matches.FirstOrDefault();

                if (selected == null || !_venueDistanceService.IsValidCoordinate(selected.Latitude, selected.Longitude))
                {
                    return UnprocessableEntity(new
                    {
                        success = false,
                        status = "NOT_VERIFIED",
                        message = "Unable to verify this venue location. Please select another location or try again."
                    });
                }

                var distance = eventDate.HasValue && startTime.HasValue
                    ? await CalculateVenueServiceabilityAsync(eventDate.Value, startTime.Value, selected.Latitude, selected.Longitude)
                    : _venueDistanceService.Calculate(selected.Latitude, selected.Longitude);

                return Json(new
                {
                    success = true,
                    status = distance.RequiresManualReview
                        ? "FOR_STAFF_REVIEW"
                        : distance.IsServiceable
                            ? "SERVICEABLE"
                            : "NOT_SERVICEABLE",
                    displayName = selected.DisplayName,
                    latitude = selected.Latitude,
                    longitude = selected.Longitude,
                    distanceKm = distance.DistanceKm,
                    travelFee = distance.TravelFee,
                    serviceZone = distance.ServiceZone,
                    isServiceable = distance.IsServiceable,
                    requiresManualReview = distance.RequiresManualReview,
                    maximumServiceDistanceKm = distance.MaximumServiceDistanceKm,
                    estimatedTravelTimeMinutes = distance.EstimatedTravelTimeMinutes,
                    availableTravelTimeMinutes = distance.AvailableTravelTimeMinutes,
                    usesPreviousEventLocation = distance.UsesPreviousEventLocation,
                    serviceabilityReason = distance.ServiceabilityReason,
                    originLatitude = distance.OriginLatitude,
                    originLongitude = distance.OriginLongitude,
                    startingPointName = distance.StartingPointName
                });
            }
            catch
            {
                return StatusCode(503, new
                {
                    success = false,
                    status = "NOT_VERIFIED",
                    message = "Unable to verify this venue location. Please select another location or try again."
                });
            }
        }

        [Authorize(Roles = "Client")]
        [HttpGet]
        public async Task<IActionResult> CreateStepOne()
        {
            var user = await _userManager.GetUserAsync(User);
            var model = new BookingStepOneViewModel
            {
                EventDate = DateTime.Today,
                ContactPerson = user?.FullName ?? string.Empty,
                ContactNumber = user?.PhoneNumber ?? string.Empty
            };

            SetCreateStepOneViewBags();

            ViewBag.UnavailableDates = await GetUnavailableBookingDatesAsync();

            return View(model);
        }

        [Authorize(Roles = "Client")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateStepOne(BookingStepOneViewModel model)
        {
            SetCreateStepOneViewBags();

            ViewBag.UnavailableDates = await GetUnavailableBookingDatesAsync();
            if (model.EventDate.Date < DateTime.Today.AddDays(3))
            {
            ModelState.AddModelError("EventDate", "Bookings must be made at least 3 days in advance.");
            }
            var requestedStart = model.EventDate.Date.Add(model.StartTime);
            var requestedEnd = requestedStart.AddHours(3);

            if (await HasBookingConflict(requestedStart, requestedEnd))
            {
                ModelState.AddModelError("StartTime", "The selected time conflicts with an existing confirmed booking.");
            }

            var isBlocked = await _context.BlockedDates
                .AnyAsync(x => x.Date.Date == model.EventDate.Date);

            if (isBlocked)
            {
                ModelState.AddModelError("EventDate", "This date is unavailable for booking.");
            }

            if (await HasReachedDailyConfirmedLimit(model.EventDate))
            {
                ModelState.AddModelError("EventDate", "This date has already reached the maximum number of bookings.");
            }

            if (model.EventDate.Date == DateTime.Today && model.StartTime < DateTime.Now.TimeOfDay)
            {
                ModelState.AddModelError("StartTime", "You cannot select a past time for today.");
            }

            ValidateEventSpecificFields(model);

            if (!string.IsNullOrWhiteSpace(model.PartyVenue))
            {
                try
                {
                    var matches = await _geocodingService.SearchAsync(model.PartyVenue);
                    var selected = matches.FirstOrDefault();
                    if (selected == null || !_venueDistanceService.IsValidCoordinate(selected.Latitude, selected.Longitude))
                    {
                        ModelState.AddModelError(nameof(model.PartyVenue), "We could not find valid coordinates for this venue. Please enter a more complete address.");
                    }
                    else
                    {
                        model.PartyVenue = selected.DisplayName;
                        model.VenueLatitude = selected.Latitude;
                        model.VenueLongitude = selected.Longitude;
                        var distance = await CalculateVenueServiceabilityAsync(model.EventDate, model.StartTime, selected.Latitude, selected.Longitude);
                        model.DistanceKm = distance.DistanceKm;
                        model.TravelFee = distance.TravelFee;
                        model.IsServiceable = distance.IsServiceable;
                        model.ServiceZone = distance.ServiceZone;
                        model.RequiresManualReview = distance.RequiresManualReview;

                        if (!distance.IsServiceable)
                        {
                            ModelState.AddModelError(nameof(model.PartyVenue), GetVenueServiceabilityError(distance));
                        }
                    }
                }
                catch (Exception)
                {
                    ModelState.AddModelError(nameof(model.PartyVenue), "The venue could not be verified right now. Please try again.");
                }
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            TempData["StepOneData"] = JsonSerializer.Serialize(model);

            return RedirectToAction(nameof(CreateStepTwo));
        }

        [Authorize(Roles = "Client")]
        [HttpGet]
        public async Task<IActionResult> CreateStepTwo(int? serviceId)
        {
            if (TempData["StepOneData"] == null)
            {
                return RedirectToAction(nameof(CreateStepOne));
            }

            var stepOneJson = TempData["StepOneData"]?.ToString();

            if (string.IsNullOrWhiteSpace(stepOneJson))
            {
                return RedirectToAction(nameof(CreateStepOne));
            }

            var stepOneModel = JsonSerializer.Deserialize<BookingStepOneViewModel>(stepOneJson);

            if (stepOneModel == null)
            {
                return RedirectToAction(nameof(CreateStepOne));
            }

            TempData["StepOneData"] = stepOneJson;

            var packages = await _context.Services
                .Include(s => s.Inclusions)
                .Where(s => !s.IsArchived)
                .OrderBy(s => s.Name)
                .ToListAsync();

            if (!packages.Any())
            {
                TempData["Error"] = "No active packages are available yet. Please contact staff.";
                return RedirectToAction(nameof(CreateStepOne));
            }

            var selectedPackage = serviceId.HasValue
                ? packages.FirstOrDefault(p => p.Id == serviceId.Value) ?? packages.First()
                : packages.First();

            var model = new BookingStepTwoViewModel
            {
                EventType = stepOneModel.EventType,
                Motif = stepOneModel.Motif,
                EventDate = stepOneModel.EventDate,
                StartTime = stepOneModel.StartTime,
                PartyTheme = stepOneModel.PartyTheme,
                PartyVenue = stepOneModel.PartyVenue,
                VenueLatitude = stepOneModel.VenueLatitude,
                VenueLongitude = stepOneModel.VenueLongitude,
                DistanceKm = stepOneModel.DistanceKm,
                TravelFee = stepOneModel.TravelFee,
                IsServiceable = stepOneModel.IsServiceable,
                ServiceZone = stepOneModel.ServiceZone,
                RequiresManualReview = stepOneModel.RequiresManualReview,
                CelebrantName = stepOneModel.CelebrantName,
                Age = stepOneModel.Age,
                PaxCount = stepOneModel.PaxCount,
                ContactPerson = stepOneModel.ContactPerson,
                ContactNumber = stepOneModel.ContactNumber,

                ServiceId = selectedPackage.Id,
                PackageName = selectedPackage.Name,
                BasePrice = selectedPackage.Price,
                FinalPrice = selectedPackage.Price + stepOneModel.TravelFee,
                RequiredDownpayment = 2000,

                AvailablePackages = packages.Select(p => new ServiceOptionViewModel
                {
                    Id = p.Id,
                    Name = p.Name,
                    Price = p.Price
                }).ToList(),

                Inclusions = selectedPackage.Inclusions.Select(i => new PackageInclusionSelectionViewModel
                {
                    Id = i.Id,
                    Name = i.Name,
                    DeductionAmount = i.DeductionAmount,
                    IsRemovable = i.IsRemovable,
                    IsSelected = true
                }).ToList()
            };

            return View(model);
        }

        [Authorize(Roles = "Client")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitBooking(BookingStepTwoViewModel model)
        {
            var packages = await _context.Services
                .Where(s => !s.IsArchived)
                .OrderBy(s => s.Name)
                .ToListAsync();

            model.AvailablePackages = packages.Select(p => new ServiceOptionViewModel
            {
                Id = p.Id,
                Name = p.Name,
                Price = p.Price
            }).ToList();

            var selectedPackage = await _context.Services
                .Include(s => s.Inclusions)
                .FirstOrDefaultAsync(s => s.Id == model.ServiceId && !s.IsArchived);

            if (selectedPackage == null)
            {
                ModelState.AddModelError("ServiceId", "Invalid package selected.");
                return View("CreateStepTwo", model);
            }

            // Never trust client-posted coordinates, distance, or travel fee. Re-geocode and recalculate on the server.
            VenueDistanceResult venueDistance;
            try
            {
                var matches = await _geocodingService.SearchAsync(model.PartyVenue);
                var selectedVenue = matches.FirstOrDefault();
                if (selectedVenue == null || !_venueDistanceService.IsValidCoordinate(selectedVenue.Latitude, selectedVenue.Longitude))
                {
                    ModelState.AddModelError(nameof(model.PartyVenue), "The venue could not be verified. Please return to Step 1 and select a valid venue.");
                    return View("CreateStepTwo", model);
                }

                model.PartyVenue = selectedVenue.DisplayName;
                model.VenueLatitude = selectedVenue.Latitude;
                model.VenueLongitude = selectedVenue.Longitude;
                venueDistance = await CalculateVenueServiceabilityAsync(model.EventDate, model.StartTime, selectedVenue.Latitude, selectedVenue.Longitude);
                model.DistanceKm = venueDistance.DistanceKm;
                model.TravelFee = venueDistance.TravelFee;
                model.IsServiceable = venueDistance.IsServiceable;
                model.ServiceZone = venueDistance.ServiceZone;
                model.RequiresManualReview = venueDistance.RequiresManualReview;

                if (!venueDistance.IsServiceable)
                {
                    ModelState.AddModelError(nameof(model.PartyVenue), GetVenueServiceabilityError(venueDistance));
                    return View("CreateStepTwo", model);
                }
            }
            catch (Exception)
            {
                ModelState.AddModelError(nameof(model.PartyVenue), "The venue could not be verified right now. Please try again.");
                return View("CreateStepTwo", model);
            }

            var isBlocked = await _context.BlockedDates
                .AnyAsync(x => x.Date.Date == model.EventDate.Date);

            if (isBlocked)
            {
                ModelState.AddModelError("EventDate", "This date is unavailable for booking.");
            }

            if (model.EventDate.Date < DateTime.Today.AddDays(3))
            {
                ModelState.AddModelError("EventDate", "Bookings must be made at least 3 days in advance.");
            }

            if (model.EventDate.Date == DateTime.Today && model.StartTime < DateTime.Now.TimeOfDay)
            {
                ModelState.AddModelError("StartTime", "You cannot select a past time for today.");
            }

            ValidateEventSpecificFields(model);

            var removedIds = model.RemovedInclusionIds ?? new List<int>();
            var allInclusions = selectedPackage.Inclusions.ToList();

            var validRemovedInclusions = allInclusions
                .Where(i => removedIds.Contains(i.Id) && i.IsRemovable)
                .ToList();

            if (validRemovedInclusions.Count > MaxRemovedInclusions)
            {
                ModelState.AddModelError(
                    "RemovedInclusionIds",
                    $"You can only remove up to {MaxRemovedInclusions} package inclusions.");
            }

            var totalDeduction = validRemovedInclusions.Any()
                ? FixedInclusionDeduction
                : 0m;

            var finalPrice = selectedPackage.Price - totalDeduction + venueDistance.TravelFee;

            if (finalPrice < 0)
            {
                finalPrice = 0;
            }

            model.PackageName = selectedPackage.Name;
            model.BasePrice = selectedPackage.Price;
            model.FinalPrice = finalPrice;
            model.RequiredDownpayment = FixedRequiredDownpayment;

            model.Inclusions = allInclusions.Select(i => new PackageInclusionSelectionViewModel
            {
                Id = i.Id,
                Name = i.Name,

                // Display fixed ₱2,000 deduction for removable inclusions.
                DeductionAmount = i.IsRemovable ? FixedInclusionDeduction : 0,

                IsRemovable = i.IsRemovable,
                IsSelected = !removedIds.Contains(i.Id)
            }).ToList();

            ModelState.Remove(nameof(model.PackageName));
            ModelState.Remove(nameof(model.AvailablePackages));
            ModelState.Remove(nameof(model.Inclusions));
            ModelState.Remove(nameof(model.RemovedInclusionIds));
            ModelState.Remove(nameof(model.BasePrice));
            ModelState.Remove(nameof(model.FinalPrice));
            ModelState.Remove(nameof(model.RequiredDownpayment));

            if (!model.AcceptTerms)
            {
                ModelState.AddModelError("AcceptTerms", "You must agree to the Terms and Agreement before submitting your booking.");
            }

            if (!ModelState.IsValid)
            {
                TempData["Error"] = string.Join(" | ",
                    ModelState.Values
                        .SelectMany(v => v.Errors)
                        .Select(e => e.ErrorMessage));

                return View("CreateStepTwo", model);
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
            var endDateTime = startDateTime.AddHours(selectedPackage.DurationInHours);

            if (await HasReachedDailyConfirmedLimit(model.EventDate))
            {
                ModelState.AddModelError("", "This date already has the maximum of 3 confirmed bookings.");
                return View("CreateStepTwo", model);
            }

            if (await HasBookingConflict(startDateTime, endDateTime))
            {
                ModelState.AddModelError("", "The selected time conflicts with an existing confirmed booking, including the 1-hour buffer.");
                return View("CreateStepTwo", model);
            }

            var removedInclusionNames = validRemovedInclusions
                .Select(i => i.Name)
                .ToList();

            var booking = new Booking
            {
                ClientId = legacyClient.Id,
                ApplicationUserId = appUserId,
                ServiceId = selectedPackage.Id,
                EventDate = model.EventDate.Date,
                StartTime = startDateTime,
                EndTime = endDateTime,
                Status = BookingStatus.Pending,
                CreatedAt = DateTime.Now,
                IsCompletedLocked = false,

                EventType = model.EventType,
                Motif = model.Motif,
                PartyTheme = model.PartyTheme,
                PartyVenue = model.PartyVenue,
                VenueLatitude = model.VenueLatitude,
                VenueLongitude = model.VenueLongitude,
                DistanceKm = venueDistance.DistanceKm,
                TravelFee = venueDistance.TravelFee,
                IsServiceable = venueDistance.IsServiceable,
                ServiceZone = venueDistance.ServiceZone,
                RequiresManualReview = venueDistance.RequiresManualReview,
                CelebrantName = model.CelebrantName,
                Age = model.Age,
                PaxCount = model.PaxCount,
                ContactPerson = model.ContactPerson,
                ContactNumber = model.ContactNumber,

                PackageName = selectedPackage.Name,
                BasePrice = selectedPackage.Price,
                FinalPrice = finalPrice,
                RequiredDownpayment = FixedRequiredDownpayment,
                RemovedInclusionsJson = JsonSerializer.Serialize(removedInclusionNames)
            };

            _context.Bookings.Add(booking);
            await _context.SaveChangesAsync();

            _context.BookingTimelines.Add(new BookingTimeline
            {
                BookingId = booking.Id,
                EventType = TimelineEventType.BookingCreated,
                Notes = $"Booking was created by client using the new 2-step booking flow. Venue distance: {venueDistance.DistanceKm:N2} KM, zone: {venueDistance.ServiceZone}, travel fee: ₱{venueDistance.TravelFee:N2}, manual review: {venueDistance.RequiresManualReview}. Removed inclusions: {removedInclusionNames.Count}. Fixed total deduction: ₱{totalDeduction:N2}.",                CreatedAt = DateTime.Now
            });

            var staffUsers = await _userManager.GetUsersInRoleAsync("Staff");
            var adminUsers = await _userManager.GetUsersInRoleAsync("Admin");

            foreach (var staff in staffUsers)
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = staff.Id,
                    Title = "New Booking Submitted",
                    Message = $"A new booking request was submitted by {appUser.FullName} for {booking.EventDate:MMMM dd, yyyy}.",
                    Link = $"/Bookings/Details/{booking.Id}",
                    IsRead = false,
                    CreatedAt = DateTime.Now
                });
            }

            foreach (var admin in adminUsers)
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = admin.Id,
                    Title = "New Booking Submitted",
                    Message = $"A new booking request was submitted by {appUser.FullName} for {booking.EventDate:MMMM dd, yyyy}.",
                    Link = $"/Bookings/Details/{booking.Id}",
                    IsRead = false,
                    CreatedAt = DateTime.Now
                });
            }

            await _context.SaveChangesAsync();

            TempData["Success"] = "Your booking request was submitted successfully. Please wait for staff review before proceeding to the payment step.";

            return RedirectToAction(nameof(MyBookings));
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

            var lockingPaymentStatuses = new[]
            {
                PaymentStatus.Pending,
                PaymentStatus.Verified
            };

            var lockedBookings = await _context.Payments
                .Include(p => p.Booking)
                .Where(p => lockingPaymentStatuses.Contains(p.Status))
                .Where(p => p.Booking != null)
                .Select(p => p.Booking!)
                .ToListAsync();

            var unavailableBookingIds = new List<int>();

            foreach (var booking in bookings)
            {
                if (booking.Status != BookingStatus.AwaitingDownpayment)
                    continue;

                var hasConflict = lockedBookings.Any(locked =>
                    locked.Id != booking.Id &&
                    booking.StartTime < locked.EndTime.AddHours(1) &&
                    booking.EndTime > locked.StartTime
                );

                if (hasConflict)
                {
                    unavailableBookingIds.Add(booking.Id);
                }
            }

            ViewBag.UnavailableBookingIds = unavailableBookingIds;
            ViewBag.FinancialSummaries = await _financialService.GetSummariesAsync(bookings.Select(b => b.Id));

            return View(bookings);
        }
        [Authorize(Roles = "Staff,Admin,Owner")]
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();
            await _historyService.ArchiveDueBookingsAsync();

            var booking = await _context.Bookings
                .Include(b => b.Client)
                .Include(b => b.Service)
                .Include(b => b.Payments)
                .Include(b => b.RescheduleRequests)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (booking == null) return NotFound();

            if (User.IsInRole("Staff") || User.IsInRole("Owner"))
                return View("StaffDetails", booking);

            ViewBag.FinancialSummary = await _financialService.GetSummaryAsync(booking.Id);
            return View(booking);
        }

        [Authorize(Roles = "Owner")]
        public async Task<IActionResult> Approve(int? id)
        {
            if (id == null) return NotFound();

            var booking = await _context.Bookings
                .Include(b => b.Client)
                .Include(b => b.Service)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (booking == null) return NotFound();

            if (booking.Status != BookingStatus.Pending)
            {
                TempData["Error"] = "Only pending bookings can be approved.";
                return RedirectToAction(nameof(Index));
            }

            return View(booking);
        }

        [HttpPost, ActionName("Approve")]
        [Authorize(Roles = "Staff,Owner")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveConfirmed(int id, string? internalNote)
        {
            var booking = await _context.Bookings
                .Include(b => b.Client)
                .Include(b => b.Service)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (booking == null) return NotFound();

            if (booking.Status != BookingStatus.Pending)
            {
                TempData["Error"] = "Only pending bookings can be approved.";
                return RedirectToAction(nameof(Index));
            }

            var now = DateTime.Now;
            var staffUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "Unknown";
            var staffAccount = await _userManager.GetUserAsync(User);
            var staffName = staffAccount?.FullName ?? User.Identity?.Name ?? "Unknown";
            var cleanNote = string.IsNullOrWhiteSpace(internalNote) ? null : internalNote.Trim();
            if (cleanNote != null)
            {
                var noteEntry = $"[{now:yyyy-MM-dd hh:mm tt}] Approved by {staffName}: {cleanNote}";
                booking.InternalNotes = string.IsNullOrWhiteSpace(booking.InternalNotes) ? noteEntry : $"{booking.InternalNotes}\n{noteEntry}";
            }

            booking.Status = BookingStatus.AwaitingDownpayment;

            _context.BookingTimelines.Add(new BookingTimeline
            {
                BookingId = booking.Id,
                EventType = TimelineEventType.BookingApproved,
                Notes = "Booking was approved and is now awaiting downpayment.",
                CreatedAt = DateTime.Now
            });

            if (!string.IsNullOrWhiteSpace(booking.ApplicationUserId))
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = booking.ApplicationUserId,
                    Title = "Booking Approved",
                    Message = $"Your booking for {booking.EventDate:MMMM dd, yyyy} was approved and is now awaiting downpayment.",
                    Link = "/Bookings/MyBookings",
                    IsRead = false,
                    CreatedAt = DateTime.Now
                });
            }

            await _context.SaveChangesAsync();
            await _activityService.LogAsync(SystemActivityType.BookingApproved,
                $"Booking #{booking.Id} approved during Staff review.", staffUserId, staffName,
                booking.Id.ToString(), "Booking", new { BookingId = booking.Id, Note = cleanNote, StaffUserId = staffUserId, StaffName = staffName, ActionTaken = "Approved", Timestamp = now });

            TempData["Success"] = "Booking approved. The client can now proceed to payment.";
            return RedirectToAction(nameof(Index));
        }

        [Authorize(Roles = "Owner")]
        public async Task<IActionResult> Decline(int? id)
        {
            if (id == null) return NotFound();

            var booking = await _context.Bookings
                .Include(b => b.Client)
                .Include(b => b.Service)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (booking == null) return NotFound();

            if (booking.Status != BookingStatus.Pending)
            {
                TempData["Error"] = "Only pending bookings can be declined.";
                return RedirectToAction(nameof(Index));
            }

            return View(booking);
        }

        [HttpPost, ActionName("Decline")]
        [Authorize(Roles = "Staff,Owner")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeclineConfirmed(int id, string? internalNote)
        {
            var booking = await _context.Bookings
                .Include(b => b.Client)
                .Include(b => b.Service)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (booking == null) return NotFound();

            if (booking.Status != BookingStatus.Pending)
            {
                TempData["Error"] = "Only pending bookings can be declined.";
                return RedirectToAction(nameof(Index));
            }

            var now = DateTime.Now;
            var staffUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "Unknown";
            var staffAccount = await _userManager.GetUserAsync(User);
            var staffName = staffAccount?.FullName ?? User.Identity?.Name ?? "Unknown";
            var cleanNote = string.IsNullOrWhiteSpace(internalNote) ? null : internalNote.Trim();
            if (cleanNote != null)
            {
                var noteEntry = $"[{now:yyyy-MM-dd hh:mm tt}] Declined by {staffName}: {cleanNote}";
                booking.InternalNotes = string.IsNullOrWhiteSpace(booking.InternalNotes) ? noteEntry : $"{booking.InternalNotes}\n{noteEntry}";
            }

            booking.Status = BookingStatus.Declined;
            booking.TrashReason = TrashReason.RejectedByAdmin;
            booking.TrashNotes = "Booking was declined during Staff review.";
            booking.ArchivedAt = DateTime.UtcNow;

            _context.BookingTimelines.Add(new BookingTimeline
            {
                BookingId = booking.Id,
                EventType = TimelineEventType.BookingDeclined,
                Notes = "Booking was declined.",
                CreatedAt = DateTime.Now
            });

            if (!string.IsNullOrWhiteSpace(booking.ApplicationUserId))
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = booking.ApplicationUserId,
                    Title = "Booking Declined",
                    Message = $"Your booking for {booking.EventDate:MMMM dd, yyyy} was declined.",
                    Link = "/Bookings/MyBookings",
                    IsRead = false,
                    CreatedAt = DateTime.Now
                });
            }

            await _context.SaveChangesAsync();
            await _activityService.LogAsync(SystemActivityType.BookingDeclined,
                $"Booking #{booking.Id} declined during Staff review.", staffUserId, staffName,
                booking.Id.ToString(), "Booking", new { BookingId = booking.Id, Note = cleanNote, StaffUserId = staffUserId, StaffName = staffName, ActionTaken = "Declined", Timestamp = now });

            TempData["Success"] = "Booking declined.";
            return RedirectToAction(nameof(Index));
        }

        [Authorize(Roles = "Owner")]
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
        [Authorize(Roles = "Owner")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddNote(int id, string? internalNotes)
        {
            var booking = await _context.Bookings.FirstOrDefaultAsync(b => b.Id == id);

            if (booking == null) return NotFound();

            booking.InternalNotes = internalNotes;
            await _context.SaveChangesAsync();

            TempData["Success"] = "Internal note saved.";
            return RedirectToAction(nameof(Index));
        }


        [Authorize(Roles = "Client")]
        public IActionResult Create()
        {
            return RedirectToAction(nameof(CreateStepOne));
        }

        private static readonly string[] ClientCancellableStatuses =
        {
            BookingStatus.Pending,
            BookingStatus.AwaitingDownpayment,
            BookingStatus.AwaitingVerification
        };

        [Authorize(Roles = "Client")]
        [HttpGet]
        public async Task<IActionResult> Cancel(int? id)
        {
            if (id == null) return NotFound();

            var appUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var booking = await _context.Bookings
                .Include(b => b.Client)
                .Include(b => b.Service)
                .FirstOrDefaultAsync(b => b.Id == id && b.ApplicationUserId == appUserId);

            if (booking == null) return NotFound();

            if (!ClientCancellableStatuses.Contains(booking.Status))
            {
                TempData["Error"] = "This booking can no longer be cancelled because it is already confirmed, completed, or otherwise finalized. Please submit a refund or reschedule request instead.";
                return RedirectToAction(nameof(MyBookings));
            }

            return View(booking);
        }

        [HttpPost, ActionName("Cancel")]
        [Authorize(Roles = "Client")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelConfirmed(int id)
        {
            var appUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var booking = await _context.Bookings
                .FirstOrDefaultAsync(b => b.Id == id && b.ApplicationUserId == appUserId);

            if (booking == null) return NotFound();

            if (!ClientCancellableStatuses.Contains(booking.Status))
            {
                TempData["Error"] = "This booking can no longer be cancelled.";
                return RedirectToAction(nameof(MyBookings));
            }

            booking.Status = BookingStatus.Cancelled;

            _context.BookingTimelines.Add(new BookingTimeline
            {
                BookingId = booking.Id,
                EventType = TimelineEventType.BookingCancelled,
                Notes = "Booking was cancelled by the client.",
                CreatedAt = DateTime.Now
            });

            var staffUsers = await _userManager.GetUsersInRoleAsync("Staff");
            var adminUsers = await _userManager.GetUsersInRoleAsync("Admin");

            foreach (var recipient in staffUsers.Concat(adminUsers))
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = recipient.Id,
                    Title = "Booking Cancelled",
                    Message = $"A client cancelled their booking for {booking.EventDate:MMMM dd, yyyy}.",
                    Link = $"/Bookings/Details/{booking.Id}",
                    IsRead = false,
                    CreatedAt = DateTime.Now
                });
            }

            await _context.SaveChangesAsync();

            TempData["Success"] = "Your booking was cancelled successfully.";
            return RedirectToAction(nameof(MyBookings));
        }

        private void SetCreateStepOneViewBags()
        {
            ViewBag.EventTypes = new List<string>
            {
                "Birthday",
                "Company Events",
                "Bridal Shower",
                "Gender Reveal",
                "School Events",
                "Halloween",
                "Christmas Parties",
                "Easter Events"
            };
        }

        private void ValidateEventSpecificFields(BookingStepOneViewModel model)
        {
            if (string.IsNullOrWhiteSpace(model.EventType))
            {
                return;
            }

            if (model.EventType == "Birthday")
            {
                if (string.IsNullOrWhiteSpace(model.PartyTheme))
                    ModelState.AddModelError("PartyTheme", "Party theme is required for birthday events.");

                if (string.IsNullOrWhiteSpace(model.CelebrantName))
                    ModelState.AddModelError("CelebrantName", "Celebrant name is required for birthday events.");

                if (model.Age == null || model.Age <= 0)
                    ModelState.AddModelError("Age", "Age is required for birthday events.");
            }
            else if (model.EventType == "Bridal Shower")
            {
                if (string.IsNullOrWhiteSpace(model.PartyTheme))
                    ModelState.AddModelError("PartyTheme", "Party theme is required for bridal shower events.");

                if (string.IsNullOrWhiteSpace(model.CelebrantName))
                    ModelState.AddModelError("CelebrantName", "Celebrant name is required for bridal shower events.");

                model.Age = null;
            }
            else if (
                model.EventType == "Gender Reveal" ||
                model.EventType == "Halloween" ||
                model.EventType == "Christmas Parties" ||
                model.EventType == "Easter Events"
            )
            {
                if (string.IsNullOrWhiteSpace(model.PartyTheme))
                    ModelState.AddModelError("PartyTheme", "Party theme is required for this event type.");

                model.CelebrantName = null;
                model.Age = null;
            }
            else if (
                model.EventType == "Company Events" ||
                model.EventType == "School Events"
            )
            {
                model.PartyTheme = null;
                model.CelebrantName = null;
                model.Age = null;
            }
        }

        private void ValidateEventSpecificFields(BookingStepTwoViewModel model)
        {
            if (string.IsNullOrWhiteSpace(model.EventType))
            {
                return;
            }

            if (model.EventType == "Birthday")
            {
                if (string.IsNullOrWhiteSpace(model.PartyTheme))
                    ModelState.AddModelError("PartyTheme", "Party theme is required for birthday events.");

                if (string.IsNullOrWhiteSpace(model.CelebrantName))
                    ModelState.AddModelError("CelebrantName", "Celebrant name is required for birthday events.");

                if (model.Age == null || model.Age <= 0)
                    ModelState.AddModelError("Age", "Age is required for birthday events.");
            }
            else if (model.EventType == "Bridal Shower")
            {
                if (string.IsNullOrWhiteSpace(model.PartyTheme))
                    ModelState.AddModelError("PartyTheme", "Party theme is required for bridal shower events.");

                if (string.IsNullOrWhiteSpace(model.CelebrantName))
                    ModelState.AddModelError("CelebrantName", "Celebrant name is required for bridal shower events.");

                model.Age = null;
            }
            else if (
                model.EventType == "Gender Reveal" ||
                model.EventType == "Halloween" ||
                model.EventType == "Christmas Parties" ||
                model.EventType == "Easter Events"
            )
            {
                if (string.IsNullOrWhiteSpace(model.PartyTheme))
                    ModelState.AddModelError("PartyTheme", "Party theme is required for this event type.");

                model.CelebrantName = null;
                model.Age = null;
            }
            else if (
                model.EventType == "Company Events" ||
                model.EventType == "School Events"
            )
            {
                model.PartyTheme = null;
                model.CelebrantName = null;
                model.Age = null;
            }
        }

        private async Task<VenueDistanceResult> CalculateVenueServiceabilityAsync(
            DateTime eventDate,
            TimeSpan startTime,
            double venueLatitude,
            double venueLongitude)
        {
            var requestedStart = eventDate.Date.Add(startTime);
            var previousBooking = await _context.Bookings
                .Where(b => b.Status == BookingStatus.Confirmed
                            && b.EventDate.Date == eventDate.Date
                            && b.StartTime < requestedStart)
                .OrderByDescending(b => b.StartTime)
                .FirstOrDefaultAsync();

            if (previousBooking == null)
            {
                return _venueDistanceService.Calculate(venueLatitude, venueLongitude);
            }

            var availableTravelTimeMinutes = Math.Max(
                0,
                (int)Math.Floor((requestedStart - previousBooking.EndTime).TotalMinutes));

            if (previousBooking.VenueLatitude is not double originLatitude
                || previousBooking.VenueLongitude is not double originLongitude
                || !_venueDistanceService.IsValidCoordinate(originLatitude, originLongitude))
            {
                return new VenueDistanceResult
                {
                    IsServiceable = false,
                    ServiceZone = "Not available",
                    MaximumServiceDistanceKm = 0,
                    AvailableTravelTimeMinutes = availableTravelTimeMinutes,
                    UsesPreviousEventLocation = true,
                    HasSufficientTravelTime = false,
                    StartingPointName = previousBooking.PartyVenue,
                    ServiceabilityReason = "The preceding confirmed event has no verified venue location, so travel time cannot be confirmed. Please select another time."
                };
            }

            var route = _venueDistanceService.Calculate(originLatitude, originLongitude, venueLatitude, venueLongitude);
            var hasSufficientTravelTime = route.EstimatedTravelTimeMinutes <= availableTravelTimeMinutes;

            return new VenueDistanceResult
            {
                DistanceKm = route.DistanceKm,
                TravelFee = route.TravelFee,
                ServiceZone = route.ServiceZone,
                IsServiceable = route.IsServiceable && hasSufficientTravelTime,
                RequiresManualReview = route.RequiresManualReview && hasSufficientTravelTime,
                MaximumServiceDistanceKm = route.MaximumServiceDistanceKm,
                EstimatedTravelTimeMinutes = route.EstimatedTravelTimeMinutes,
                AvailableTravelTimeMinutes = availableTravelTimeMinutes,
                UsesPreviousEventLocation = true,
                HasSufficientTravelTime = hasSufficientTravelTime,
                OriginLatitude = route.OriginLatitude,
                OriginLongitude = route.OriginLongitude,
                StartingPointName = previousBooking.PartyVenue,
                ServiceabilityReason = hasSufficientTravelTime
                    ? null
                    : "There is not enough travel time from the preceding confirmed event to the selected start time. Please select another time or venue."
            };
        }

        private static string GetVenueServiceabilityError(VenueDistanceResult distance)
        {
            return distance.ServiceabilityReason
                   ?? $"This venue is outside the maximum service distance of {distance.MaximumServiceDistanceKm:0.##} KM. Please select another venue or contact Aries Magic for assistance.";
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

        private async Task<List<string>> GetUnavailableBookingDatesAsync()
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

        [Authorize(Roles = "Client")]
        [HttpGet]
        public async Task<IActionResult> CheckDateAvailability(DateTime date)
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
        public async Task<IActionResult> GetUnavailableTimeRanges(DateTime date)
        {
            var confirmedBookings = await _context.Bookings
                .Where(b => b.Status == BookingStatus.Confirmed && b.EventDate.Date == date.Date)
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
