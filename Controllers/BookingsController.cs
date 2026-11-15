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
        private const string CurrentTermsVersion = "2026-11-08-v3";
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
        public async Task<IActionResult> Index(string? search, string? bookingStatus, string? paymentStatus, DateTime? eventDate, int? serviceId, int page = 1, int pageSize = 10, string? sortBy = null)
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
                // Action queue, not a second payment-verification or historical booking list.
                // Keep pending venue-review and customer-acceptance records discoverable,
                // but never expose Owner-only payment states here.
                bookingsQuery = bookingsQuery.Where(b => b.Status == BookingStatus.Pending);
                bookingStatus = bookingStatus is "RequiresReview" or "Pending" or "AllRequests"
                    ? bookingStatus : "AllRequests";
                if (bookingStatus == "RequiresReview")
                    bookingsQuery = bookingsQuery.Where(b => b.RequiresManualReview
                        || b.TravelFeeStatus == VenueLocationPolicy.PendingReview
                        || b.TravelFeeStatus == VenueLocationPolicy.AwaitingCustomerAcceptance);
                // This date filter was intentionally removed from the Staff UI.
                eventDate = null;
                serviceId = null;
                sortBy = sortBy == "oldest" ? "oldest" : "newest";
                ViewBag.StaffSort = sortBy;
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

            search = search?.Trim();
            pageSize = 5; // Round 3 fixed page size, unrelated filters remain database-backed.
            page = Math.Max(1, page);
            int totalCount;
            List<Booking> bookings;
            if (isStaff)
            {
                if (!string.IsNullOrWhiteSpace(search))
                {
                    var term = search.Length > 100 ? search[..100] : search;
                    search = term;
                    // Keep Booking ID search correct (BK-year-id) without fetching the full queue.
                    if (term.StartsWith("BK-", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = term.Split('-', 3);
                        if (parts.Length >= 2 && int.TryParse(parts[1], out var year))
                        {
                            bookingsQuery = bookingsQuery.Where(b => b.CreatedAt.Year == year);
                            if (parts.Length == 3 && int.TryParse(parts[2], out var bookingId))
                                bookingsQuery = bookingsQuery.Where(b => b.Id == bookingId);
                            else if (parts.Length == 3 && parts[2].Length > 0)
                            {
                                var suffix = parts[2].TrimStart('0');
                                if (suffix.Length == 0) suffix = "0";
                                bookingsQuery = bookingsQuery.Where(b => b.Id.ToString().Contains(suffix));
                            }
                        }
                        else
                            bookingsQuery = bookingsQuery.Where(b => false);
                    }
                    else
                    {
                        bookingsQuery = bookingsQuery.Where(b =>
                            b.Id.ToString().Contains(term)
                            || (b.Client != null && (b.Client.FullName.Contains(term) || b.Client.Email.Contains(term)))
                            || (b.Service != null && b.Service.Name.Contains(term))
                            || b.PartyVenue.Contains(term));
                    }
                }
                totalCount = await bookingsQuery.CountAsync();
                var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
                page = Math.Min(page, totalPages);
                bookings = await (sortBy == "oldest"
                    ? bookingsQuery.OrderBy(b => b.CreatedAt).ThenBy(b => b.Id)
                    : bookingsQuery.OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.Id))
                    .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            }
            else
            {
                bookings = await bookingsQuery.OrderByDescending(b => b.CreatedAt).ToListAsync();
                if (!string.IsNullOrWhiteSpace(search))
                {
                    var lowered = search.ToLowerInvariant();
                    bookings = bookings.Where(b =>
                        $"BK-{b.CreatedAt.Year}-{b.Id:D3}".Contains(search, StringComparison.OrdinalIgnoreCase) ||
                        (b.Client?.FullName?.ToLowerInvariant().Contains(lowered) ?? false) ||
                        (b.Client?.Email?.ToLowerInvariant().Contains(lowered) ?? false) ||
                        (b.Service?.Name?.ToLowerInvariant().Contains(lowered) ?? false)
                    ).ToList();
                }
                totalCount = bookings.Count;
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
                    RequiresManualReview = b.RequiresManualReview,
                    TravelFeeStatus = b.TravelFeeStatus
                };
            }).ToList();

            if (!isStaff && !string.IsNullOrWhiteSpace(paymentStatus) && paymentStatus != "All")
            {
                rows = rows.Where(r => r.PaymentStatus == paymentStatus).ToList();
            }

            var viewModel = new BookingManagementViewModel
            {
                Search = search,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
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
        [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("account")]
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
                    longitude = r.Longitude,
                    isPrecise = r.IsPrecise
                }));
            }
            catch
            {
                return StatusCode(503, new { message = "Venue search is temporarily unavailable." });
            }
        }

        private sealed record VenueEvaluation(
            string Address, string Method, double? Latitude, double? Longitude,
            VenueDistanceResult? Distance, string VerificationStatus, string FeeStatus,
            bool IsServiceable, string Message);

        private async Task<VenueEvaluation> EvaluateVenueAsync(
            string? venue, string? method, double? latitude, double? longitude,
            DateTime date, TimeSpan time, string? street = null, string? barangay = null,
            string? city = null, string? province = null, string? postal = null,
            string? venueName = null)
        {
            var selection = VenueLocationPolicy.IsMethod(method) ? method! : VenueLocationPolicy.Search;
            if (selection == VenueLocationPolicy.Manual)
            {
                if (!VenueLocationPolicy.SufficientManualAddress(street, barangay, city, province))
                    throw new ArgumentException("Enter a city, province, and a street, subdivision, or barangay.");
                var address = VenueLocationPolicy.FormatManualAddress(street, barangay, city, province, postal, venueName);
                if (address.Length > 500)
                    throw new ArgumentException("The address is too long. Shorten it and add directions in the optional field.");
                return new VenueEvaluation(address, selection, null, null, null,
                    VenueLocationPolicy.NeedsStaffReview, VenueLocationPolicy.PendingReview, true,
                    "Manual address received. Staff must confirm service coverage and travel charges before payment.");
            }

            if (string.IsNullOrWhiteSpace(venue) || venue.Length > 500)
                throw new ArgumentException("Enter a complete event address or choose manual entry.");

            if (!VenueLocationPolicy.HasCoordinatePair(latitude, longitude) ||
                !VenueLocationPolicy.InPhilippines(latitude!.Value, longitude!.Value))
            {
                if (selection == VenueLocationPolicy.MapPin)
                    throw new ArgumentException("Place your map pin within the Philippines and confirm its position.");
                // Never silently select the provider's first match as the exact venue.
                // Old forms without coordinates can still request a booking, but the address
                // and service area must be reviewed by Staff before a quote or payment.
                return new VenueEvaluation(venue.Trim(), selection, null, null, null,
                    VenueLocationPolicy.NeedsStaffReview, VenueLocationPolicy.PendingReview, true,
                    "No exact venue pin was confirmed. Staff will review the address and travel charges.");
            }

            if (selection == VenueLocationPolicy.MapPin)
                return new VenueEvaluation(venue.Trim(), selection, latitude, longitude, null,
                    VenueLocationPolicy.CustomerConfirmed, VenueLocationPolicy.PendingReview, true,
                    "Customer-placed pin received. Staff must confirm location and travel charges.");

            try
            {
                var matches = await _geocodingService.SearchAsync(venue.Trim());
                var selected = matches.FirstOrDefault(r =>
                    string.Equals(r.DisplayName.Trim(),venue.Trim(),StringComparison.OrdinalIgnoreCase)
                    && Math.Abs(r.Latitude - latitude!.Value) < 0.0002
                    && Math.Abs(r.Longitude - longitude!.Value) < 0.0002);
                if (selected is null || !selected.IsPrecise)
                    return new VenueEvaluation(venue.Trim(), selection, latitude, longitude, null,
                        VenueLocationPolicy.NeedsStaffReview, VenueLocationPolicy.PendingReview, true,
                        "Search result is approximate or could not be reconfirmed. Staff will clarify the exact venue.");
                var distance = await CalculateVenueServiceabilityAsync(date, time, selected.Latitude, selected.Longitude);
                return new VenueEvaluation(venue.Trim(), selection, selected.Latitude, selected.Longitude,
                    distance, VenueLocationPolicy.CustomerConfirmed, VenueLocationPolicy.Calculated,
                    distance.IsServiceable, distance.IsServiceable
                        ? "Customer confirmed a precise search result. Distance is an estimate, not a routed road distance."
                        : GetVenueServiceabilityError(distance));
            }
            catch (HttpRequestException)
            {
                return new VenueEvaluation(venue.Trim(), selection, latitude, longitude, null,
                    VenueLocationPolicy.NeedsStaffReview, VenueLocationPolicy.PendingReview, true,
                    "Location search is temporarily unavailable. Staff will confirm your address.");
            }
            catch (TaskCanceledException)
            {
                return new VenueEvaluation(venue.Trim(), selection, latitude, longitude, null,
                    VenueLocationPolicy.NeedsStaffReview, VenueLocationPolicy.PendingReview, true,
                    "The map search timed out. Staff will review your address.");
            }
        }

        [Authorize(Roles = "Client")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CheckVenueServiceability(
            string? partyVenue, DateTime? eventDate, TimeSpan? startTime,
            string? venueSelectionMethod, double? venueLatitude, double? venueLongitude,
            string? manualStreet, string? manualBarangay, string? manualCity,
            string? manualProvince, string? manualPostalCode, string? manualVenueName)
        {
            try
            {
                if (!eventDate.HasValue || !startTime.HasValue)
                    return BadRequest(new { success=false, message="Select your event date and time." });
                var eval = await EvaluateVenueAsync(partyVenue,venueSelectionMethod,
                    venueLatitude,venueLongitude,eventDate.Value,startTime.Value,
                    manualStreet,manualBarangay,manualCity,manualProvince,manualPostalCode,manualVenueName);
                var distance = eval.Distance;
                return Json(new
                {
                    success=true,
                    status=!eval.IsServiceable ? "NOT_SERVICEABLE"
                        : eval.FeeStatus==VenueLocationPolicy.PendingReview ? "PENDING_LOCATION_REVIEW"
                        : distance?.RequiresManualReview==true ? "FOR_STAFF_REVIEW" : "SERVICEABLE",
                    displayName=eval.Address,
                    latitude=eval.Latitude,
                    longitude=eval.Longitude,
                    locationVerificationStatus=eval.VerificationStatus,
                    travelFeeStatus=eval.FeeStatus,
                    travelFeePending=eval.FeeStatus==VenueLocationPolicy.PendingReview,
                    distanceKm=distance?.DistanceKm,
                    travelFee=distance?.TravelFee,
                    serviceZone=distance?.ServiceZone,
                    isServiceable=eval.IsServiceable,
                    requiresManualReview=eval.FeeStatus==VenueLocationPolicy.PendingReview
                        || distance?.RequiresManualReview==true,
                    estimatedTravelTimeMinutes=distance?.EstimatedTravelTimeMinutes,
                    availableTravelTimeMinutes=distance?.AvailableTravelTimeMinutes,
                    usesPreviousEventLocation=distance?.UsesPreviousEventLocation ?? false,
                    serviceabilityReason=eval.Message,
                    startingPointName=distance?.UsesPreviousEventLocation==true
                        ? "Previous confirmed event" : distance?.StartingPointName
                });
            }
            catch (ArgumentException e)
            {
                return UnprocessableEntity(new { success=false, message=e.Message });
            }
            catch
            {
                return StatusCode(503,new { success=false,
                    message="Travel checks are temporarily unavailable. Please try again or enter your address manually." });
            }
        }

        [Authorize(Roles = "Client")]
        [HttpGet]
        public async Task<IActionResult> CreateStepOne(int? serviceId)
        {
            var user = await _userManager.GetUserAsync(User);
            return await ClientWizardViewAsync(new BookingStepTwoViewModel
            {
                EventDate = DateTime.Today.AddDays(3),
                StartTime = new TimeSpan(10, 0, 0),
                ContactPerson = user?.FullName ?? string.Empty,
                ContactNumber = user?.PhoneNumber ?? string.Empty,
                PaxCount = 1,
                ServiceId = serviceId ?? 0
            });
        }

        private async Task<IActionResult> ClientWizardViewAsync(BookingStepTwoViewModel model)
        {
            var packages = await _context.Services.AsNoTracking().Include(s => s.Inclusions)
                .Where(s => !s.IsArchived).OrderBy(s => s.Price).ToListAsync();
            ViewBag.WizardPackages = packages;
            SetCreateStepOneViewBags();
            return View("Create", model);
        }

        [Authorize(Roles = "Client")]
        [HttpGet]
        public async Task<IActionResult> CheckScheduleAvailability(DateTime date, TimeSpan startTime, int? serviceId)
        {
            if (!ModelState.IsValid || date.Date < DateTime.Today.AddDays(3) || startTime < TimeSpan.Zero || startTime >= TimeSpan.FromDays(1))
                return Json(new { available = false, message = "Choose a valid time and a date at least 3 days from today." });
            if (await _context.BlockedDates.AnyAsync(d => d.Date.Date == date.Date) || await HasReachedDailyConfirmedLimit(date))
                return Json(new { available = false, message = "This date is unavailable. Please choose another date." });
            var packages = await _context.Services.AsNoTracking().Where(s => !s.IsArchived).ToListAsync();
            if (serviceId.HasValue && serviceId.Value > 0) packages = packages.Where(s => s.Id == serviceId.Value).ToList();
            var availableIds = new List<int>();
            var start = date.Date.Add(startTime);
            foreach (var package in packages)
                if (!await HasBookingConflict(start, start.AddHours(package.DurationInHours))) availableIds.Add(package.Id);
            return Json(new { available = availableIds.Count > 0, availableIds,
                message = availableIds.Count > 0 ? "This time is available to request. Your booking is subject to review and payment confirmation." : "No package fits this time. Please choose another time." });
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
                return await ClientWizardViewAsync(model);
            }

            if (!model.CustomerConfirmedLocation)
            {
                ModelState.AddModelError(nameof(model.CustomerConfirmedLocation), "Confirm the selected location or manual address before submitting.");
                return await ClientWizardViewAsync(model);
            }
            if (!VenueLocationPolicy.IsMethod(model.VenueSelectionMethod))
            {
                ModelState.AddModelError(nameof(model.VenueSelectionMethod), "Choose a supported location method.");
                return await ClientWizardViewAsync(model);
            }
            // Never trust client-posted distance, price or serviceability. Search selections
            // are matched to the specific geocoder result; customer pins and manual addresses
            // always require staff resolution before payment.
            VenueEvaluation evaluated;
            try
            {
                evaluated = await EvaluateVenueAsync(model.PartyVenue, model.VenueSelectionMethod,
                    model.VenueLatitude, model.VenueLongitude, model.EventDate, model.StartTime,
                    model.ManualStreet, model.ManualBarangay, model.ManualCity,
                    model.ManualProvince, model.ManualPostalCode, model.ManualVenueName);
            }
            catch (ArgumentException ex)
            {
                ModelState.AddModelError(nameof(model.PartyVenue), ex.Message);
                return await ClientWizardViewAsync(model);
            }
            if (!evaluated.IsServiceable)
            {
                ModelState.AddModelError(nameof(model.PartyVenue), evaluated.Message);
                return await ClientWizardViewAsync(model);
            }
            model.PartyVenue = evaluated.Address;
            ModelState.Remove(nameof(model.PartyVenue));
            model.VenueLatitude = evaluated.Latitude;
            model.VenueLongitude = evaluated.Longitude;
            var venueDistance = evaluated.Distance;
            model.DistanceKm = venueDistance?.DistanceKm;
            model.TravelFee = venueDistance?.TravelFee ?? 0m;
            model.IsServiceable = evaluated.IsServiceable;
            model.ServiceZone = venueDistance?.ServiceZone;
            model.RequiresManualReview = evaluated.FeeStatus==VenueLocationPolicy.PendingReview
                || venueDistance?.RequiresManualReview==true;

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

            if (model.StartTime < TimeSpan.Zero || model.StartTime >= TimeSpan.FromDays(1))
                ModelState.AddModelError(nameof(model.StartTime), "Choose a valid event time.");

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

            var finalPrice = selectedPackage.Price - totalDeduction + (venueDistance?.TravelFee ?? 0m);

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
            ModelState.Remove(nameof(model.BasePrice));
            ModelState.Remove(nameof(model.FinalPrice));
            ModelState.Remove(nameof(model.RequiredDownpayment));

            if (!model.AcceptTerms || !string.Equals(model.AcceptedTermsVersion, CurrentTermsVersion, StringComparison.Ordinal))
            {
                ModelState.AddModelError("AcceptTerms", "Please review and accept the current Terms and Conditions before submitting your booking.");
            }

            if (!ModelState.IsValid)
            {
                TempData["Error"] = string.Join(" | ",
                    ModelState.Values
                        .SelectMany(v => v.Errors)
                        .Select(e => e.ErrorMessage));

                return await ClientWizardViewAsync(model);
            }

            var appUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var appUser = await _userManager.GetUserAsync(User);

            if (string.IsNullOrEmpty(appUserId) || appUser == null)
            {
                return Challenge();
            }

            await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
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

            var removedInclusionNames = validRemovedInclusions.OrderBy(i => i.Id).Select(i => i.Name).ToList();
            var removedInclusionsJson = JsonSerializer.Serialize(removedInclusionNames);
            // An exact replay of an active request is not another booking. The serializable
            // predicate also coordinates simultaneous retries without a new schema or lock service.
            var duplicateId = await _context.Bookings.AsNoTracking()
                .Where(b => b.ApplicationUserId == appUserId && b.ServiceId == selectedPackage.Id
                    && b.EventDate == model.EventDate.Date && b.StartTime == startDateTime && b.EndTime == endDateTime
                    && b.PartyVenue == model.PartyVenue && b.EventType == model.EventType && b.Motif == model.Motif
                    && b.PartyTheme == model.PartyTheme && b.CelebrantName == model.CelebrantName && b.Age == model.Age
                    && b.PaxCount == model.PaxCount && b.ContactPerson == model.ContactPerson && b.ContactNumber == model.ContactNumber
                    && b.RemovedInclusionsJson == removedInclusionsJson
                    && (b.Status == BookingStatus.Pending || b.Status == BookingStatus.AwaitingDownpayment
                        || b.Status == BookingStatus.AwaitingVerification || b.Status == BookingStatus.Confirmed))
                .Select(b => (int?)b.Id).FirstOrDefaultAsync();
            if (duplicateId.HasValue)
            {
                TempData["Success"] = "This booking request has already been submitted. Review its current status in My Bookings.";
                return RedirectToAction(nameof(MyBookings));
            }

            if (await HasReachedDailyConfirmedLimit(model.EventDate))
            {
                ModelState.AddModelError("", "This date already has the maximum of 3 confirmed bookings.");
                return await ClientWizardViewAsync(model);
            }

            if (await HasBookingConflict(startDateTime, endDateTime))
            {
                ModelState.AddModelError("", "The selected time conflicts with an existing confirmed booking, including the 1-hour buffer.");
                return await ClientWizardViewAsync(model);
            }

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
                Motif = model.Motif ?? string.Empty,
                PartyTheme = model.PartyTheme,
                PartyVenue = model.PartyVenue,
                VenueLatitude = model.VenueLatitude,
                VenueLongitude = model.VenueLongitude,
                DistanceKm = venueDistance?.DistanceKm,
                TravelFee = venueDistance?.TravelFee ?? 0m,
                IsServiceable = evaluated.IsServiceable,
                ServiceZone = venueDistance?.ServiceZone,
                RequiresManualReview = model.RequiresManualReview,
                VenueSelectionMethod = evaluated.Method,
                VenueVerificationStatus = evaluated.VerificationStatus,
                TravelFeeStatus = evaluated.FeeStatus,
                VenueDirections = model.VenueDirections?.Trim(),
                VenueManualDetails = evaluated.Method == VenueLocationPolicy.Manual ? evaluated.Address : null,
                CelebrantName = model.CelebrantName,
                Age = model.Age,
                PaxCount = model.PaxCount,
                ContactPerson = model.ContactPerson,
                ContactNumber = model.ContactNumber,

                PackageName = selectedPackage.Name,
                BasePrice = selectedPackage.Price,
                FinalPrice = finalPrice,
                RequiredDownpayment = FixedRequiredDownpayment,
                RemovedInclusionsJson = removedInclusionsJson,
                AcceptedTermsVersion = CurrentTermsVersion,
                TermsAcceptedAtUtc = DateTime.UtcNow
            };

            _context.Bookings.Add(booking);
            await _context.SaveChangesAsync();

            _context.BookingTimelines.Add(new BookingTimeline
            {
                BookingId = booking.Id,
                EventType = TimelineEventType.BookingCreated,
                Notes = $"Customer-provided location method: {evaluated.Method}; travel charge status: {evaluated.FeeStatus}; distance: {(venueDistance is null ? "pending" : $"{venueDistance.DistanceKm:N2} km")}; fee: {(venueDistance is null ? "pending" : $"PHP {venueDistance.TravelFee:N2}")}. Fee cannot be collected until location review and customer acceptance as required.",
                CreatedAt = DateTime.Now
            });

            var staffUsers = await _userManager.GetUsersInRoleAsync("Staff");
            var ownerUsers = await _userManager.GetUsersInRoleAsync("Owner");

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

            foreach (var owner in ownerUsers.Where(x=>x.IsActive))
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = owner.Id,
                    Title = "New Booking Submitted",
                    Message = $"A new booking request was submitted by {appUser.FullName} for {booking.EventDate:MMMM dd, yyyy}.",
                    Link = $"/Bookings/Details/{booking.Id}",
                    IsRead = false,
                    CreatedAt = DateTime.Now
                });
            }

            await _context.SaveChangesAsync();

            TempData["Success"] = "Your booking request was submitted successfully. Please wait for staff review before proceeding to the payment step.";
            await transaction.CommitAsync();
            return RedirectToAction(nameof(MyBookings));
        }

        [Authorize(Roles = "Client")]
        public IActionResult Dashboard() => RedirectToAction(nameof(MyBookings));

        [Authorize(Roles = "Client")]
        public async Task<IActionResult> MyBookings()
        {
            // Reconcile after an idle host restart; the background worker also processes these.
            await HttpContext.RequestServices.GetRequiredService<IBookingExpirationService>()
                .ExpireOverdueBookingsAsync();
            var appUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var bookings = await _context.Bookings.AsNoTracking()
                .Include(b => b.Client)
                .Include(b => b.Service)
                .Include(b => b.Payments)
                .Include(b => b.RescheduleRequests)
                .AsSplitQuery()
                .Where(b => b.ApplicationUserId == appUserId)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();

            var unavailableBookingIds = await GetUnavailableBookingIdsAsync(bookings);

            ViewBag.UnavailableBookingIds = unavailableBookingIds;
            ViewBag.PaymentDeadlines = await InitialPaymentWindowPolicy.GetDeadlinesUtcAsync(
                _context, bookings.Select(b => b.Id));
            ViewBag.FinancialSummaries = await _financialService.GetSummariesAsync(bookings.Select(b => b.Id));

            return View(bookings);
        }
        private async Task<List<int>> GetUnavailableBookingIdsAsync(IEnumerable<Booking> bookings)
        {
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

            return unavailableBookingIds;
        }

        [Authorize(Roles = "Client,Staff,Owner")]
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();
            await _historyService.ArchiveDueBookingsAsync();
            await HttpContext.RequestServices.GetRequiredService<IBookingExpirationService>()
                .ExpireOverdueBookingsAsync();

            var booking = await _context.Bookings
                .Include(b => b.Client)
                .Include(b => b.Service)
                .Include(b => b.Payments)
                .ThenInclude(p => p.OcrVerifications)
                .Include(b => b.RescheduleRequests)
                .Include(b => b.TimelineEvents)
                .FirstOrDefaultAsync(b => b.Id == id && (!User.IsInRole("Client") || b.ApplicationUserId == User.FindFirstValue(ClaimTypes.NameIdentifier)));

            if (booking == null) return NotFound();
            ViewBag.PendingConfirmedChange = booking.Status == BookingStatus.Confirmed
                ? await ConfirmedBookingChangePolicy.PendingAsync(_context, booking.Id) : null;

            if (User.IsInRole("Staff") || User.IsInRole("Owner"))
            {
                ViewBag.CancellationRequestLinked = booking.CancellationRequestMessageId.HasValue
                    && await _context.Messages.AnyAsync(m => m.Id == booking.CancellationRequestMessageId
                        && m.SenderId == booking.CancellationRequestByUserId
                        && m.Conversation != null &&
                        ((m.Conversation.BookingId == booking.Id && m.Conversation.ConversationType == ConversationTypes.Booking)
                         || (m.RelatedBookingId == booking.Id &&
                             m.Conversation.ConversationType == ConversationTypes.ClientSupport &&
                             m.Conversation.SupportClientId == booking.ApplicationUserId)));
                if (User.IsInRole("Owner"))
                {
                    ViewBag.FinancialSummary = await _financialService.GetSummaryAsync(booking.Id);
                    ViewBag.OwnerRefunds = await _context.RefundRequests.AsNoTracking()
                        .Where(r => r.BookingId == booking.Id)
                        .OrderByDescending(r => r.RequestedAt).ToListAsync();
                }
                return View("StaffDetails", booking);
            }

            ViewBag.IsScheduleUnavailable = (await GetUnavailableBookingIdsAsync(new[] { booking })).Contains(booking.Id);
            ViewBag.PaymentDeadline = await InitialPaymentWindowPolicy.GetDeadlineUtcAsync(_context, booking.Id);
            ViewBag.FinancialSummary = await _financialService.GetSummaryAsync(booking.Id);
            var initialPayment = booking.Payments.Where(p => p.Status == PaymentStatus.Verified && p.InitialVerificationAtUtc.HasValue)
                .OrderBy(p => p.InitialVerificationAtUtc).FirstOrDefault();
            var deadline = initialPayment?.InitialVerificationAtUtc?.AddHours(24);
            ViewBag.CanDirectCancel = deadline.HasValue && DateTime.UtcNow < deadline.Value
                && ClientCancellableStatuses.Contains(booking.Status) && !booking.RefundEligible
                && !booking.Payments.Any(p => p.Status is PaymentStatus.Pending or PaymentStatus.AdditionalEvidenceRequired);
            ViewBag.CancellationDeadline = deadline;
            ViewBag.CanRequestCancellationReview = deadline.HasValue && DateTime.UtcNow >= deadline.Value
                && ClientCancellableStatuses.Contains(booking.Status) && !booking.RefundEligible
                && !booking.Payments.Any(p => p.Status is PaymentStatus.Pending or PaymentStatus.AdditionalEvidenceRequired);
            ViewBag.CancellationConversationId = booking.CancellationRequestMessageId.HasValue
                ? await _context.Conversations.Where(c => c.SupportClientId == booking.ApplicationUserId &&
                    c.ConversationType == ConversationTypes.ClientSupport)
                    .Select(c => (int?)c.Id).FirstOrDefaultAsync()
                : null;
            ViewBag.HasRefundEligibility = booking.RefundEligible && booking.RefundEligibilityPaymentId.HasValue
                && !await _context.RefundRequests.AnyAsync(r => r.OriginalPaymentId == booking.RefundEligibilityPaymentId);
            if (User.IsInRole("Client"))
                ViewBag.ClientRefunds = await _context.RefundRequests.AsNoTracking()
                    .Where(r => r.BookingId == booking.Id).OrderByDescending(r => r.RequestedAt).ToListAsync();
            return View("ClientDetails", booking);
        }

        [HttpPost]
        [Authorize(Roles = "Staff,Owner")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResolveVenue(int id, double? latitude, double? longitude,
            decimal? confirmedTravelFee, string? correctedAddress, string? reviewReason)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var booking = await _context.Bookings.FirstOrDefaultAsync(x => x.Id == id);
            if (booking == null) return NotFound();
            if (booking.Status != BookingStatus.Pending ||
                booking.TravelFeeStatus is not (VenueLocationPolicy.PendingReview or VenueLocationPolicy.AwaitingCustomerAcceptance))
            {
                TempData["Error"]="Only pending location-review requests may be corrected.";
                return RedirectToAction(nameof(Details),new { id });
            }
            if (!VenueLocationPolicy.HasCoordinatePair(latitude,longitude) ||
                !VenueLocationPolicy.InPhilippines(latitude!.Value,longitude!.Value) ||
                string.IsNullOrWhiteSpace(reviewReason) || reviewReason.Trim().Length is < 10 or > 700 ||
                (correctedAddress is { Length: > 500 }) ||
                (confirmedTravelFee.HasValue && (confirmedTravelFee.Value < 0m || confirmedTravelFee.Value > 25000m
                    || confirmedTravelFee.Value != decimal.Round(confirmedTravelFee.Value,2))))
            {
                TempData["Error"]="Provide a valid Philippine venue pin, review reason (10-700 characters), and valid travel fee.";
                return RedirectToAction(nameof(Details),new { id });
            }
            var distance=await CalculateVenueServiceabilityAsync(booking.EventDate,booking.StartTime.TimeOfDay,latitude.Value,longitude.Value);
            if (!distance.IsServiceable)
            {
                TempData["Error"]=GetVenueServiceabilityError(distance);
                return RedirectToAction(nameof(Details),new { id });
            }
            if (await _context.Payments.AnyAsync(x=>x.BookingId==id
                && (x.Status == PaymentStatus.Pending || x.Status == PaymentStatus.Verified || x.Status == PaymentStatus.AdditionalEvidenceRequired)))
            {
                TempData["Error"]="A payment already exists; location changes require a separate financial review.";
                return RedirectToAction(nameof(Details),new { id });
            }
            var oldTravelFee=booking.TravelFee;
            var approvedFee=confirmedTravelFee ?? distance.TravelFee;
            booking.TravelFee=approvedFee;
            booking.FinalPrice=Math.Max(0m,booking.FinalPrice-oldTravelFee+approvedFee);
            booking.DistanceKm=distance.DistanceKm;
            booking.VenueLatitude=latitude;
            booking.VenueLongitude=longitude;
            booking.IsServiceable=true;
            booking.ServiceZone=distance.ServiceZone;
            booking.RequiresManualReview=distance.RequiresManualReview;
            booking.VenueVerificationStatus=VenueLocationPolicy.StaffVerified;
            booking.TravelFeeStatus=VenueLocationPolicy.AwaitingCustomerAcceptance;
            booking.TravelQuoteAcceptedAtUtc=null;
            booking.VenueStaffReviewedAtUtc=DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(correctedAddress)) booking.PartyVenue=correctedAddress.Trim();
            var reviewer=User.FindFirstValue(ClaimTypes.NameIdentifier)??"Unknown";
            var note=$"[{DateTime.UtcNow:O}] Staff venue resolution by {reviewer}: {reviewReason.Trim()}; "
                + $"distance {distance.DistanceKm:N2} km; preliminary quote PHP {approvedFee:N2}; awaiting customer acceptance.";
            booking.InternalNotes=string.IsNullOrWhiteSpace(booking.InternalNotes)
                ? note : booking.InternalNotes+"\n"+note;
            if (!string.IsNullOrWhiteSpace(booking.ApplicationUserId))
                _context.Notifications.Add(new Notification {
                    UserId=booking.ApplicationUserId,
                    Title="Confirm your updated event location and travel quote",
                    Message="Staff reviewed your venue. Review and accept the updated travel fee and booking total before payment becomes available.",
                    Link=$"/Bookings/Details/{id}",IsRead=false,CreatedAt=DateTime.Now
                });
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            TempData["Success"]="Venue reviewed. The customer must accept the updated booking quote before Staff can approve payment.";
            return RedirectToAction(nameof(Details),new { id });
        }

        [HttpPost]
        [Authorize(Roles = "Client")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AcceptVenueQuote(int id, decimal quotedTotal, bool acceptTravelQuote)
        {
            await using var transaction=await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var userId=User.FindFirstValue(ClaimTypes.NameIdentifier);
            var booking=await _context.Bookings.FirstOrDefaultAsync(b=>b.Id==id&&b.ApplicationUserId==userId);
            if (booking==null) return NotFound();
            if (booking.Status!=BookingStatus.Pending ||
                booking.TravelFeeStatus!=VenueLocationPolicy.AwaitingCustomerAcceptance ||
                !acceptTravelQuote || quotedTotal!=booking.FinalPrice || !booking.IsServiceable)
            {
                TempData["Error"]="This quote has changed or needs review. Check the latest total before accepting.";
                return RedirectToAction(nameof(Details),new { id });
            }
            booking.TravelFeeStatus=VenueLocationPolicy.Accepted;
            booking.TravelQuoteAcceptedAtUtc=DateTime.UtcNow;
            var staff=await _userManager.GetUsersInRoleAsync("Staff");
            var owners=await _userManager.GetUsersInRoleAsync("Owner");
            foreach(var person in staff.Concat(owners).DistinctBy(x=>x.Id))
                _context.Notifications.Add(new Notification {
                    UserId=person.Id,Title="Customer accepted revised venue quote",
                    Message=$"Booking #{id} location and travel quote were accepted; Staff may continue reviewing the booking.",
                    Link=$"/Bookings/Details/{id}",IsRead=false,CreatedAt=DateTime.Now
                });
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            TempData["Success"]="You accepted the reviewed travel quote. Staff can now continue evaluating your booking.";
            return RedirectToAction(nameof(Details),new { id });
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
            await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
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

            if (!VenueLocationPolicy.CanOpenPayment(booking.TravelFeeStatus))
            {
                TempData["Error"]="The venue and travel quote must be reviewed and accepted before payment can be enabled.";
                return RedirectToAction(nameof(Details),new { id });
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
            var paymentOpensAtUtc = DateTime.UtcNow;
            var paymentDeadlineUtc = paymentOpensAtUtc.Add(InitialPaymentWindowPolicy.Duration);
            _context.BookingTimelines.Add(new BookingTimeline
            {
                BookingId = booking.Id,
                EventType = TimelineEventType.PaymentWindowOpened,
                Notes = $"Staff approval opened payment at {paymentOpensAtUtc:O}; deadline {paymentDeadlineUtc:O}.",
                CreatedAt = paymentOpensAtUtc
            });

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
                    Message = $"Your booking for {booking.EventDate:MMMM dd, yyyy} was approved. Upload a valid initial payment proof by {InitialPaymentWindowPolicy.FormatManila(paymentDeadlineUtc)} or the request will expire. Owner verification can follow after timely submission.",
                    Link = "/Bookings/MyBookings",
                    IsRead = false,
                    CreatedAt = DateTime.Now
                });
            }

            await _context.SaveChangesAsync();
            await _activityService.LogAsync(SystemActivityType.BookingApproved,
                $"Booking #{booking.Id} approved during Staff review.", staffUserId, staffName,
                booking.Id.ToString(), "Booking", new { BookingId = booking.Id, Note = cleanNote, StaffUserId = staffUserId, StaffName = staffName, ActionTaken = "Approved", PreviousStatus = BookingStatus.Pending, NewStatus = BookingStatus.AwaitingDownpayment, Timestamp = now });
            await transaction.CommitAsync();
            TempData["Success"] = $"Booking approved. Client payment proof is due by {InitialPaymentWindowPolicy.FormatManila(paymentDeadlineUtc)}.";
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
            await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
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
                booking.Id.ToString(), "Booking", new { BookingId = booking.Id, Note = cleanNote, StaffUserId = staffUserId, StaffName = staffName, ActionTaken = "Declined", PreviousStatus = BookingStatus.Pending, NewStatus = BookingStatus.Declined, Timestamp = now });
            await transaction.CommitAsync();
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
            BookingStatus.AwaitingVerification,
            BookingStatus.Confirmed
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
                .Include(b => b.Payments)
                .FirstOrDefaultAsync(b => b.Id == id && b.ApplicationUserId == appUserId);

            if (booking == null) return NotFound();

            var verifiedInitial = booking.Payments.Where(p => p.Status == PaymentStatus.Verified && p.InitialVerificationAtUtc.HasValue)
                .OrderBy(p => p.InitialVerificationAtUtc).FirstOrDefault();
            if (booking.Payments.Any(p => p.Status is PaymentStatus.Pending or PaymentStatus.AdditionalEvidenceRequired)
                || !BookingCancellationPolicy.IsClientDirectCancellationAllowed(booking, verifiedInitial, DateTime.UtcNow))
            {
                TempData["Error"] = verifiedInitial == null
                    ? "Direct cancellation is available only after the Owner verifies the initial payment. Contact Staff to request help."
                    : "Your 24-hour cancellation window has ended. Contact Staff to request a review.";
                return RedirectToAction(nameof(MyBookings));
            }

            ViewBag.CancellationDeadline = verifiedInitial!.InitialVerificationAtUtc!.Value.AddHours(24);
            return View(booking);
        }

        [HttpPost, ActionName("Cancel")]
        [Authorize(Roles = "Client")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelConfirmed(int id)
        {
            var appUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var booking = await _context.Bookings.Include(b => b.Payments)
                .FirstOrDefaultAsync(b => b.Id == id && b.ApplicationUserId == appUserId);

            if (booking == null) return NotFound();

            var verifiedInitial = booking.Payments.Where(p => p.Status == PaymentStatus.Verified && p.InitialVerificationAtUtc.HasValue)
                .OrderBy(p => p.InitialVerificationAtUtc).FirstOrDefault();
            if (booking.Payments.Any(p => p.Status is PaymentStatus.Pending or PaymentStatus.AdditionalEvidenceRequired)
                || !BookingCancellationPolicy.IsClientDirectCancellationAllowed(booking, verifiedInitial, DateTime.UtcNow))
            {
                TempData["Error"] = "This booking is outside the direct cancellation window or is no longer eligible. Contact Staff if you need a cancellation review.";
                return RedirectToAction(nameof(MyBookings));
            }

            var previousStatus = booking.Status;
            booking.CancelledAtUtc = DateTime.UtcNow;
            booking.CancelledByUserId = appUserId;
            booking.CancellationActorRole = "Client";
            booking.CancellationReason = "Client direct cancellation within 24 hours of initial payment verification.";
            booking.CancellationSource = "Client24HourWindow";
            booking.CancellationPreviousStatus = previousStatus;
            booking.CancellationPaymentId = verifiedInitial!.Id;
            booking.RefundEligible = true;
            booking.RefundEligibilityReason = "ClientCancellationWithin24Hours";
            booking.RefundEligibilityPaymentId = verifiedInitial.Id;
            booking.Status = BookingStatus.Cancelled;

            _context.BookingTimelines.Add(new BookingTimeline
            {
                BookingId = booking.Id,
                EventType = TimelineEventType.BookingCancelled,
                Notes = "Booking was cancelled by the client within the verified initial-payment cancellation window.",
                CreatedAt = DateTime.UtcNow
            });

            var staffUsers = await _userManager.GetUsersInRoleAsync("Staff");
            var ownerUsers = await _userManager.GetUsersInRoleAsync("Owner");

            foreach (var recipient in staffUsers.Concat(ownerUsers).Where(x=>x.IsActive).DistinctBy(x=>x.Id))
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = recipient.Id,
                    Title = "Booking Cancelled",
                    Message = $"A client cancelled their booking for {booking.EventDate:MMMM dd, yyyy}.",
                    Link = $"/Bookings/Details/{booking.Id}",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();
            await _activityService.LogAsync(SystemActivityType.BookingCancelled,
                $"Booking #{booking.Id} cancelled by Client.", appUserId!, User.Identity?.Name,
                booking.Id.ToString(), "Booking", new { BookingId = booking.Id, ClientId = appUserId, ActorRole = "Client", Reason = booking.CancellationReason,
                    PreviousStatus = previousStatus, NewStatus = booking.Status, AtUtc = booking.CancelledAtUtc,
                    PaymentId = verifiedInitial.Id, RefundEligible = true, RefundEligibilityReason = booking.RefundEligibilityReason });
            await transaction.CommitAsync();

            TempData["Success"] = "Your booking was cancelled successfully.";
            return RedirectToAction(nameof(MyBookings));
        }

        [HttpPost]
        [Authorize(Roles = "Staff")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelAfterReview(int id, string? reason)
        {
            reason = reason?.Trim();
            if (string.IsNullOrWhiteSpace(reason) || reason.Length < 8 || reason.Length > 1000)
            {
                TempData["Error"] = "Record a cancellation reason of at least 8 characters.";
                return RedirectToAction(nameof(Details), new { id });
            }

            await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var booking = await _context.Bookings.Include(b => b.Payments).FirstOrDefaultAsync(b => b.Id == id);
            if (booking == null) return NotFound();
            var verifiedInitial = booking.Payments.Where(p => p.Status == PaymentStatus.Verified && p.InitialVerificationAtUtc.HasValue)
                .OrderBy(p => p.InitialVerificationAtUtc).FirstOrDefault();
            var linkedRequestExists = booking.CancellationRequestMessageId.HasValue
                && await _context.Messages.AnyAsync(m => m.Id == booking.CancellationRequestMessageId
                    && m.SenderId == booking.CancellationRequestByUserId
                    && m.Conversation != null &&
                    ((m.Conversation.BookingId == booking.Id && m.Conversation.ConversationType == ConversationTypes.Booking)
                     || (m.RelatedBookingId == booking.Id &&
                         m.Conversation.ConversationType == ConversationTypes.ClientSupport &&
                         m.Conversation.SupportClientId == booking.ApplicationUserId)));
            if (!ClientCancellableStatuses.Contains(booking.Status) || booking.RefundEligible
                || booking.Payments.Any(p => p.Status is PaymentStatus.Pending or PaymentStatus.AdditionalEvidenceRequired)
                || verifiedInitial == null || booking.Status is BookingStatus.Cancelled or BookingStatus.Completed
                || !linkedRequestExists
                || DateTime.UtcNow < verifiedInitial.InitialVerificationAtUtc!.Value.AddHours(24))
            {
                TempData["Error"] = "This booking is not eligible for Staff cancellation review.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var actor = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var previousStatus = booking.Status;
            booking.Status = BookingStatus.Cancelled;
            booking.CancelledAtUtc = DateTime.UtcNow;
            booking.CancelledByUserId = actor;
            booking.CancellationActorRole = "Staff";
            booking.CancellationReason = reason;
            booking.CancellationSource = "StaffApprovedClientRequest";
            booking.CancellationPreviousStatus = previousStatus;
            booking.CancellationPaymentId = verifiedInitial.Id;
            booking.RefundEligible = true;
            booking.RefundEligibilityReason = "StaffApprovedCancellation";
            booking.RefundEligibilityPaymentId = verifiedInitial.Id;
            _context.BookingTimelines.Add(new BookingTimeline { BookingId = booking.Id, EventType = TimelineEventType.BookingCancelled,
                Notes = "Staff-approved client cancellation: " + reason, CreatedAt = DateTime.UtcNow });
            await _context.SaveChangesAsync();
            if (!string.IsNullOrWhiteSpace(booking.ApplicationUserId))
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = booking.ApplicationUserId,
                    Title = "Cancellation Approved for Refund Review",
                    Message = "Staff recorded your cancellation request. The Owner must still review any refund; no refund has been approved or paid.",
                    Link = "/Payments/MyRefundRequests",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();
            }
            await _activityService.LogAsync(SystemActivityType.BookingCancelled, $"Booking #{booking.Id} cancelled by Staff after client request.",
                actor, User.Identity?.Name, booking.Id.ToString(), "Booking", new { BookingId = booking.Id, ClientId = booking.ApplicationUserId,
                    ActorRole = "Staff", Reason = reason, PreviousStatus = previousStatus, NewStatus = booking.Status,
                    AtUtc = booking.CancelledAtUtc, PaymentId = verifiedInitial.Id,
                    CancellationRequestMessageId = booking.CancellationRequestMessageId, ClientRequestReason = booking.CancellationRequestReason, RefundEligible = true,
                    RefundEligibilityReason = booking.RefundEligibilityReason });
            await transaction.CommitAsync();
            TempData["Success"] = "The cancellation was recorded and is eligible for Owner refund review. No refund was approved or paid.";
            return RedirectToAction(nameof(Details), new { id });
        }

        private void SetCreateStepOneViewBags()
        {
            ViewBag.EventTypes = BookingEventDetailsPolicy.EventTypes.ToList();
        }

        private void ValidateEventSpecificFields(BookingStepOneViewModel model)
        {
            var normalized = BookingEventDetailsPolicy.ValidateAndNormalize(
                model.EventType, model.Motif, model.PartyTheme, model.CelebrantName, model.Age, ModelState);
            model.Motif = normalized.Motif;
            model.PartyTheme = normalized.Theme;
            model.CelebrantName = normalized.Celebrant;
            model.Age = normalized.Age;
        }

        private void ValidateEventSpecificFields(BookingStepTwoViewModel model)
        {
            var normalized = BookingEventDetailsPolicy.ValidateAndNormalize(
                model.EventType, model.Motif, model.PartyTheme, model.CelebrantName, model.Age, ModelState);
            model.Motif = normalized.Motif;
            model.PartyTheme = normalized.Theme;
            model.CelebrantName = normalized.Celebrant;
            model.Age = normalized.Age;
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
            var maxPerDay = BookingRules.MaximumDailyBookings;

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

            var defaultMax = BookingRules.MaximumDailyBookings;

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
                    var limit = defaultMax;
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

            var maxPerDay = BookingRules.MaximumDailyBookings;

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
