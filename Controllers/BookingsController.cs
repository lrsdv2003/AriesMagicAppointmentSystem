using System.Security.Claims;
using System.Text.Json;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
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

        public BookingsController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        [Authorize(Roles = "Staff,Admin")]
        public IActionResult Pending()
        {
            return RedirectToAction(nameof(Index), new { bookingStatus = "Pending" });
        }

        [Authorize(Roles = "Staff,Admin")]
        public async Task<IActionResult> Index(string? search, string? bookingStatus, string? paymentStatus)
        {
            var bookingsQuery = _context.Bookings
                .Include(b => b.Client)
                .Include(b => b.Service)
                .Include(b => b.Payments)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var lowered = search.ToLower();

                bookingsQuery = bookingsQuery.Where(b =>
                    ($"BK-{b.CreatedAt.Year}-{b.Id:D3}").ToLower().Contains(lowered) ||
                    (b.Client != null && b.Client.FullName.ToLower().Contains(lowered)) ||
                    (b.Client != null && b.Client.Email.ToLower().Contains(lowered)) ||
                    (b.Service != null && b.Service.Name.ToLower().Contains(lowered)));
            }

            if (!string.IsNullOrWhiteSpace(bookingStatus) && bookingStatus != "All")
            {
                bookingsQuery = bookingsQuery.Where(b => b.Status == bookingStatus);
            }

            var bookings = await bookingsQuery
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();

            var rows = bookings.Select(b =>
            {
                var latestPayment = b.Payments
                    .OrderByDescending(p => p.UploadedAt)
                    .FirstOrDefault();

                var latestPaymentStatus = latestPayment?.Status ?? "No Payment";

                return new BookingManagementRowViewModel
                {
                    Id = b.Id,
                    BookingCode = $"BK-{b.CreatedAt.Year}-{b.Id:D3}",
                    ClientName = b.Client?.FullName ?? "N/A",
                    ServiceName = b.Service?.Name ?? "N/A",
                    EventDate = b.EventDate,
                    BookingStatus = b.Status,
                    PaymentStatus = latestPaymentStatus,
                    InternalNotes = b.InternalNotes
                };
            }).ToList();

            if (!string.IsNullOrWhiteSpace(paymentStatus) && paymentStatus != "All")
            {
                rows = rows.Where(r => r.PaymentStatus == paymentStatus).ToList();
            }

            var viewModel = new BookingManagementViewModel
            {
                Search = search,
                BookingStatus = bookingStatus ?? "All",
                PaymentStatus = paymentStatus ?? "All",
                Bookings = rows
            };

            return View(viewModel);
        }

        [Authorize(Roles = "Client")]
        [HttpGet]
        public IActionResult CreateStepOne()
        {
            var model = new BookingStepOneViewModel
            {
                EventDate = DateTime.Today
            };

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

            return View(model);
        }

        [Authorize(Roles = "Client")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult CreateStepOne(BookingStepOneViewModel model)
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

            if (model.EventDate.Date < DateTime.Today)
            {
                ModelState.AddModelError("EventDate", "You cannot select a past date.");
            }

            if (model.EventDate.Date == DateTime.Today && model.StartTime < DateTime.Now.TimeOfDay)
            {
                ModelState.AddModelError("StartTime", "You cannot select a past time for today.");
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
                CelebrantName = stepOneModel.CelebrantName,
                Age = stepOneModel.Age,
                PaxCount = stepOneModel.PaxCount,
                ContactPerson = stepOneModel.ContactPerson,
                ContactNumber = stepOneModel.ContactNumber,

                ServiceId = selectedPackage.Id,
                PackageName = selectedPackage.Name,
                BasePrice = selectedPackage.Price,
                FinalPrice = selectedPackage.Price,
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

            if (model.EventDate.Date < DateTime.Today)
            {
                ModelState.AddModelError("EventDate", "You cannot select a past date.");
            }

            if (model.EventDate.Date == DateTime.Today && model.StartTime < DateTime.Now.TimeOfDay)
            {
                ModelState.AddModelError("StartTime", "You cannot select a past time for today.");
            }

            var removedIds = model.RemovedInclusionIds ?? new List<int>();
            var allInclusions = selectedPackage.Inclusions.ToList();

            var totalDeduction = allInclusions
                .Where(i => removedIds.Contains(i.Id) && i.IsRemovable)
                .Sum(i => i.DeductionAmount);

            model.PackageName = selectedPackage.Name;
            model.BasePrice = selectedPackage.Price;
            model.FinalPrice = selectedPackage.Price - totalDeduction;
            model.RequiredDownpayment = 2000;

            model.Inclusions = allInclusions.Select(i => new PackageInclusionSelectionViewModel
            {
                Id = i.Id,
                Name = i.Name,
                DeductionAmount = i.DeductionAmount,
                IsRemovable = i.IsRemovable,
                IsSelected = !removedIds.Contains(i.Id)
            }).ToList();

            if (!ModelState.IsValid)
            {
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

            var removedInclusionNames = allInclusions
                .Where(i => removedIds.Contains(i.Id))
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
                CelebrantName = model.CelebrantName,
                Age = model.Age,
                PaxCount = model.PaxCount,
                ContactPerson = model.ContactPerson,
                ContactNumber = model.ContactNumber,

                PackageName = selectedPackage.Name,
                BasePrice = selectedPackage.Price,
                FinalPrice = model.FinalPrice,
                RequiredDownpayment = 2000,
                RemovedInclusionsJson = JsonSerializer.Serialize(removedInclusionNames)
            };

            _context.Bookings.Add(booking);
            await _context.SaveChangesAsync();

            _context.BookingTimelines.Add(new BookingTimeline
            {
                BookingId = booking.Id,
                EventType = TimelineEventType.BookingCreated,
                Notes = "Booking was created by client using the new 2-step booking flow.",
                CreatedAt = DateTime.Now
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
                    IsRead = false,
                    CreatedAt = DateTime.Now
                });
            }

            await _context.SaveChangesAsync();

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

            return View(bookings);
        }

        [Authorize(Roles = "Staff,Admin")]
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var booking = await _context.Bookings
                .Include(b => b.Client)
                .Include(b => b.Service)
                .Include(b => b.Payments)
                .Include(b => b.RescheduleRequests)
                .FirstOrDefaultAsync(b => b.Id == id);

            if (booking == null) return NotFound();

            return View(booking);
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

            if (booking.Status != BookingStatus.Pending)
            {
                TempData["Error"] = "Only pending bookings can be approved.";
                return RedirectToAction(nameof(Index));
            }

            booking.Status = BookingStatus.AwaitingDownpayment;

            _context.BookingTimelines.Add(new BookingTimeline
            {
                BookingId = booking.Id,
                EventType = TimelineEventType.BookingApproved,
                Notes = "Booking was approved by staff and is now awaiting downpayment.",
                CreatedAt = DateTime.Now
            });

            if (!string.IsNullOrWhiteSpace(booking.ApplicationUserId))
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = booking.ApplicationUserId,
                    Title = "Booking Approved",
                    Message = $"Your booking for {booking.EventDate:MMMM dd, yyyy} was approved and is now awaiting downpayment.",
                    IsRead = false,
                    CreatedAt = DateTime.Now
                });
            }

            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
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

            if (booking.Status != BookingStatus.Pending)
            {
                TempData["Error"] = "Only pending bookings can be declined.";
                return RedirectToAction(nameof(Index));
            }

            booking.Status = BookingStatus.Declined;

            _context.BookingTimelines.Add(new BookingTimeline
            {
                BookingId = booking.Id,
                EventType = TimelineEventType.BookingDeclined,
                Notes = "Booking was declined by staff.",
                CreatedAt = DateTime.Now
            });

            if (!string.IsNullOrWhiteSpace(booking.ApplicationUserId))
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = booking.ApplicationUserId,
                    Title = "Booking Declined",
                    Message = $"Your booking for {booking.EventDate:MMMM dd, yyyy} was declined.",
                    IsRead = false,
                    CreatedAt = DateTime.Now
                });
            }

            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        [Authorize(Roles = "Client")]
        public IActionResult Create()
        {
            return RedirectToAction(nameof(CreateStepOne));
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
    }
}