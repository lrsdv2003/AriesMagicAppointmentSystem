using System.Security.Claims;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AriesMagicAppointmentSystem.Controllers
{
    public class PaymentsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly IEmailService _emailService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ContractPdfService _contractPdfService;

        public PaymentsController(
            ApplicationDbContext context,
            IWebHostEnvironment environment,
            IEmailService emailService,
            UserManager<ApplicationUser> userManager,
            ContractPdfService contractPdfService)
        {
            _context = context;
            _environment = environment;
            _emailService = emailService;
            _userManager = userManager;
            _contractPdfService = contractPdfService;
        }

        [Authorize(Roles = "Client")]
        public async Task<IActionResult> Upload()
        {
            var appUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var viewModel = new PaymentUploadViewModel
            {
                Bookings = await GetAwaitingDownpaymentBookingsAsync(appUserId)
            };

            return View(viewModel);
        }

        [HttpPost]
        [Authorize(Roles = "Client")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Upload(PaymentUploadViewModel model)
        {
            var appUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!ModelState.IsValid)
            {
                model.Bookings = await GetAwaitingDownpaymentBookingsAsync(appUserId);
                return View(model);
            }

            var booking = await _context.Bookings
                .FirstOrDefaultAsync(b => b.Id == model.BookingId && b.ApplicationUserId == appUserId);

            if (booking == null || booking.Status != BookingStatus.AwaitingDownpayment)
            {
                ModelState.AddModelError("", "Selected booking is invalid for payment upload.");
                model.Bookings = await GetAwaitingDownpaymentBookingsAsync(appUserId);
                return View(model);
            }
            var requestedStart = booking.StartTime;
            var requestedEnd = booking.EndTime;

            if (await HasPaidScheduleConflict(
                booking.Id,
                requestedStart,
                requestedEnd))
            {
                ModelState.AddModelError(
                    "",
                    "This schedule is no longer available because another client has already submitted payment for the same date and time.");

                model.Bookings = await GetAwaitingDownpaymentBookingsAsync(appUserId);

                return View(model);
            }
            if (model.ProofImage == null || model.ProofImage.Length == 0)
            {
                ModelState.AddModelError("", "Please upload a proof image.");
                model.Bookings = await GetAwaitingDownpaymentBookingsAsync(appUserId);
                return View(model);
            }

            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
            var extension = Path.GetExtension(model.ProofImage.FileName).ToLowerInvariant();

            if (!allowedExtensions.Contains(extension))
            {
                ModelState.AddModelError("", "Only JPG, JPEG, PNG, and WEBP files are allowed.");
                model.Bookings = await GetAwaitingDownpaymentBookingsAsync(appUserId);
                return View(model);
            }

            var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "payments");
            Directory.CreateDirectory(uploadsFolder);

            var uniqueFileName = $"{Guid.NewGuid()}{extension}";
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await model.ProofImage.CopyToAsync(stream);
            }

            var relativePath = $"/uploads/payments/{uniqueFileName}";

            var payment = new Payment
            {
                BookingId = model.BookingId,
                Amount = model.Amount,
                ProofImagePath = relativePath,
                Status = PaymentStatus.Pending,
                UploadedAt = DateTime.Now
            };

            _context.Payments.Add(payment);

            booking.Status = BookingStatus.AwaitingVerification;

            _context.BookingTimelines.Add(new BookingTimeline
            {
                BookingId = booking.Id,
                EventType = TimelineEventType.DownpaymentUploaded,
                Notes = "Client uploaded payment proof.",
                CreatedAt = DateTime.Now
            });

            await _context.SaveChangesAsync();
            await NotifyAffectedClientsAboutLockedScheduleAsync(booking);

            var adminIdentityUsers = await _userManager.GetUsersInRoleAsync("Admin");

            foreach (var admin in adminIdentityUsers)
            {
                await CreateNotificationAsync(
                    admin.Id,
                    "Payment Awaiting Verification",
                    "A client uploaded a payment proof that needs verification.",
                    "/Payments/PendingVerification");
            }

            var adminEmails = adminIdentityUsers
                .Where(u => !string.IsNullOrWhiteSpace(u.Email))
                .Select(u => u.Email!)
                .ToList();

            foreach (var email in adminEmails)
            {
                await _emailService.SendEmailAsync(
                    email,
                    "Payment Awaiting Verification",
                    @"
                    <h2>Payment Awaiting Verification</h2>
                    <p>A client uploaded a payment proof that requires verification.</p>
                    <p>Please check the admin payment verification page.</p>");
            }
            TempData["Success"] = "Your payment proof was uploaded successfully. Please wait for admin verification.";
            return RedirectToAction(nameof(MyUploads));
        }

        [Authorize(Roles = "Client")]
        public async Task<IActionResult> MyUploads()
        {
            var appUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var payments = await _context.Payments
                .Include(p => p.Booking)
                    .ThenInclude(b => b!.Client)
                .Include(p => p.Booking)
                    .ThenInclude(b => b!.Service)
                .Where(p => p.Booking != null && p.Booking.ApplicationUserId == appUserId)
                .OrderByDescending(p => p.UploadedAt)
                .ToListAsync();

            return View(payments);
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> PendingVerification()
        {
            var pendingPayments = await _context.Payments
                .Include(p => p.Booking)
                    .ThenInclude(b => b!.Client)
                .Include(p => p.Booking)
                    .ThenInclude(b => b!.Service)
                .Where(p => p.Status == PaymentStatus.Pending)
                .OrderByDescending(p => p.UploadedAt)
                .ToListAsync();

            return View(pendingPayments);
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Verify(int? id)
        {
            if (id == null) return NotFound();

            var payment = await _context.Payments
                .Include(p => p.Booking)
                    .ThenInclude(b => b!.Client)
                .Include(p => p.Booking)
                    .ThenInclude(b => b!.Service)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (payment == null) return NotFound();

            return View(payment);
        }

        [HttpPost, ActionName("Verify")]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyConfirmed(int id)
        {
            var payment = await _context.Payments
                .Include(p => p.Booking)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (payment == null) return NotFound();

            payment.Status = PaymentStatus.Verified;
            payment.VerifiedAt = DateTime.Now;

            if (payment.Booking != null)
            {
                payment.Booking.Status = BookingStatus.Confirmed;

                _context.BookingTimelines.Add(new BookingTimeline
                {
                    BookingId = payment.Booking.Id,
                    EventType = TimelineEventType.PaymentVerified,
                    Notes = "Admin verified payment proof.",
                    CreatedAt = DateTime.Now
                });

                _context.BookingTimelines.Add(new BookingTimeline
                {
                    BookingId = payment.Booking.Id,
                    EventType = TimelineEventType.BookingConfirmed,
                    Notes = "Booking confirmed after verified downpayment.",
                    CreatedAt = DateTime.Now
                });
            }

            await _context.SaveChangesAsync();

            if (payment.Booking != null && !string.IsNullOrWhiteSpace(payment.Booking.ApplicationUserId))
            {
                await CreateNotificationAsync(
                    payment.Booking.ApplicationUserId,
                    "Payment Verified",
                    "Your payment has been verified and your booking is now confirmed.",
                    "/Payments/MyUploads");
            }

            if (payment.Booking != null)
            {
                var bookingWithClient = await _context.Bookings
                    .Include(b => b.Client)
                    .Include(b => b.Service)
                    .FirstOrDefaultAsync(b => b.Id == payment.Booking.Id);

                if (bookingWithClient != null &&
                    bookingWithClient.Client != null &&
                    !string.IsNullOrWhiteSpace(bookingWithClient.Client.Email))
                {
                    var contractPdf =
                        _contractPdfService.GenerateContractPdf(bookingWithClient);
                        var testPath = Path.Combine(
                        Directory.GetCurrentDirectory(),
                        "wwwroot",
                        $"test-contract-BK-{bookingWithClient.CreatedAt.Year}-{bookingWithClient.Id:D3}.pdf");

                    await System.IO.File.WriteAllBytesAsync(testPath, contractPdf);

                    await _emailService.SendEmailWithAttachmentAsync(
                        bookingWithClient.Client.Email,
                        "Aries Magic - Booking Confirmation and Contract Agreement",
                        $@"
                        <h2>Booking Confirmed</h2>
                        <p>Your payment has been verified successfully.</p>
                        <p>Your booking is now confirmed.</p>
                        <p>Attached is your official Contract Agreement and Booking Receipt.</p>
                        <p>Thank you for choosing Aries Magic.</p>",
                        contractPdf,
                        $"ContractAgreement-BK-{bookingWithClient.CreatedAt.Year}-{bookingWithClient.Id:D3}.pdf");
                }
            }

            return RedirectToAction(nameof(PendingVerification));
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Reject(int? id)
        {
            if (id == null) return NotFound();

            var payment = await _context.Payments
                .Include(p => p.Booking)
                    .ThenInclude(b => b!.Client)
                .Include(p => p.Booking)
                    .ThenInclude(b => b!.Service)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (payment == null) return NotFound();

            return View(payment);
        }

        [HttpPost, ActionName("Reject")]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectConfirmed(int id, string? rejectionReason)
        {
            var payment = await _context.Payments
                .Include(p => p.Booking)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (payment == null) return NotFound();

            payment.Status = PaymentStatus.Rejected;
            payment.RejectionReason = rejectionReason;
            payment.VerifiedAt = DateTime.Now;

            if (payment.Booking != null)
            {
                payment.Booking.Status = BookingStatus.AwaitingDownpayment;
            }

            await _context.SaveChangesAsync();

            if (payment.Booking != null && !string.IsNullOrWhiteSpace(payment.Booking.ApplicationUserId))
            {
                await CreateNotificationAsync(
                    payment.Booking.ApplicationUserId,
                    "Payment Rejected",
                    "Your payment proof was rejected. Please upload a new downpayment proof.",
                    "/Payments/MyUploads");
            }

            if (payment.Booking != null)
            {
                var bookingWithClient = await _context.Bookings
                    .Include(b => b.Client)
                    .FirstOrDefaultAsync(b => b.Id == payment.Booking.Id);

                if (bookingWithClient != null && bookingWithClient.Client != null)
                {
                    await _emailService.SendEmailAsync(
                        bookingWithClient.Client.Email,
                        "Payment Rejected",
                        $@"
                        <h2>Your Payment Was Rejected</h2>
                        <p>Your payment proof was rejected.</p>
                        <p>Reason: {rejectionReason}</p>
                        <p>Please upload a new proof of downpayment.</p>");
                }
            }
            TempData["Error"] = "Your payment proof was rejected. Please review the remarks and upload a new payment proof.";
            return RedirectToAction(nameof(MyUploads));
        }

        private async Task<List<SelectListItem>> GetAwaitingDownpaymentBookingsAsync(string? appUserId)
        {
            return await _context.Bookings
                .Include(b => b.Client)
                .Include(b => b.Service)
                .Where(b => b.Status == BookingStatus.AwaitingDownpayment
                         && b.ApplicationUserId == appUserId)
                .Select(b => new SelectListItem
                {
                    Value = b.Id.ToString(),
                    Text = b.Service!.Name + " - " + b.EventDate.ToString("MMM dd, yyyy")
                })
                .ToListAsync();
        }

        private async Task CreateNotificationAsync(string userId, string title, string message, string? link = null)
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
        private async Task<bool> HasPaidScheduleConflict(int currentBookingId, DateTime requestedStart, DateTime requestedEnd)
        {
            var lockingPaymentStatuses = new[]
            {
                PaymentStatus.Pending,
                PaymentStatus.Verified
            };

            var lockedBookings = await _context.Payments
                .Include(p => p.Booking)
                .Where(p => p.BookingId != currentBookingId)
                .Where(p => lockingPaymentStatuses.Contains(p.Status))
                .Where(p => p.Booking != null)
                .Select(p => p.Booking!)
                .ToListAsync();

            foreach (var lockedBooking in lockedBookings)
            {
                var existingStart = lockedBooking.StartTime;
                var existingEndWithBuffer = lockedBooking.EndTime.AddHours(1);

                bool overlaps =
                    requestedStart < existingEndWithBuffer &&
                    requestedEnd > existingStart;

                if (overlaps)
                {
                    return true;
                }
            }

            return false;
        }
        private async Task NotifyAffectedClientsAboutLockedScheduleAsync(Booking paidBooking)
        {
            var affectedBookings = await _context.Bookings
                .Include(b => b.Client)
                .Where(b => b.Id != paidBooking.Id)
                .Where(b => b.Status == BookingStatus.AwaitingDownpayment)
                .Where(b =>
                    paidBooking.StartTime < b.EndTime.AddHours(1) &&
                    paidBooking.EndTime > b.StartTime)
                .ToListAsync();

            foreach (var affectedBooking in affectedBookings)
            {
                if (!string.IsNullOrWhiteSpace(affectedBooking.ApplicationUserId))
                {
                    _context.Notifications.Add(new Notification
                    {
                        UserId = affectedBooking.ApplicationUserId,
                        Title = "Schedule No Longer Available",
                        Message = $"Another client has already submitted payment for your selected schedule on {affectedBooking.EventDate:MMMM dd, yyyy}. Please choose another date and time.",
                        Link = "/Bookings/MyBookings",
                        IsRead = false,
                        CreatedAt = DateTime.Now
                    });
                }

                if (affectedBooking.Client != null &&
                    !string.IsNullOrWhiteSpace(affectedBooking.Client.Email))
                {
                    await _emailService.SendEmailAsync(
                        affectedBooking.Client.Email,
                        "Schedule No Longer Available",
                        $@"
                        <h2>Schedule No Longer Available</h2>
                        <p>Another client has already submitted payment for your selected schedule.</p>
                        <p><strong>Event Date:</strong> {affectedBooking.EventDate:MMMM dd, yyyy}</p>
                        <p><strong>Time:</strong> {affectedBooking.StartTime:hh:mm tt} - {affectedBooking.EndTime:hh:mm tt}</p>
                        <p>Please choose another available date and time or contact support for assistance.</p>");
                }
            }

            await _context.SaveChangesAsync();
        }
    }
}