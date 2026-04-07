using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AriesMagicAppointmentSystem.Controllers
{
    public class PaymentsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly IEmailService _emailService;

        public PaymentsController(ApplicationDbContext context, IWebHostEnvironment environment, IEmailService emailService)
        {
            _context = context;
            _environment = environment;
            _emailService = emailService;
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

            var adminUsers = await _context.LegacyUsers
                .Where(u => u.Role == "Admin")
                .ToListAsync();

            foreach (var admin in adminUsers)
            {
                await CreateNotificationAsync(
                    admin.Id,
                    "Payment Awaiting Verification",
                    "A client uploaded a payment proof that needs verification.",
                    "/Payments/PendingVerification");
            }

            var adminEmails = await _context.LegacyUsers
                .Where(u => u.Role == "Admin")
                .Select(u => u.Email)
                .ToListAsync();

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

            if (payment.Booking != null)
            {
                await CreateNotificationAsync(
                    payment.Booking.ClientId,
                    "Payment Verified",
                    "Your payment has been verified and your booking is now confirmed.",
                    "/Bookings/MyBookings");
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
                        "Payment Verified and Booking Confirmed",
                        @"
                        <h2>Your Payment Was Verified</h2>
                        <p>Your payment has been verified and your booking is now confirmed.</p>
                        <p>Thank you for your downpayment.</p>");
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

            if (payment.Booking != null)
            {
                await CreateNotificationAsync(
                    payment.Booking.ClientId,
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

            return RedirectToAction(nameof(PendingVerification));
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
        private async Task CreateNotificationAsync(int userId, string title, string message, string? link = null)
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
    }
}