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
                FixedDownpaymentAmount = FixedDownpaymentAmount,
                GCashQrPath = "wwwroot/images/payments/gcash-qr.jpeg",
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

            model.FixedDownpaymentAmount = FixedDownpaymentAmount;
            model.GCashQrPath = "wwwroot/images/payments/gcash-qr.jpeg";

            if (!ModelState.IsValid)
            {
                model.Bookings = await GetAwaitingDownpaymentBookingsAsync(appUserId);
                return View(model);
            }

            var booking = await _context.Bookings
                .Include(b => b.Service)
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
                ModelState.AddModelError("", "This slot has already been secured by another confirmed booking. If you already sent your downpayment, please submit a refund request.");
                model.Bookings = await GetAwaitingDownpaymentBookingsAsync(appUserId);
                return View(model);
            }

            var fileValidationError = await ValidateProofImageAsync(model.ProofImage);

            if (!string.IsNullOrWhiteSpace(fileValidationError))
            {
                ModelState.AddModelError("", fileValidationError);
                model.Bookings = await GetAwaitingDownpaymentBookingsAsync(appUserId);
                return View(model);
            }

            var extension = Path.GetExtension(model.ProofImage.FileName).ToLowerInvariant();

            var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "payments");
            Directory.CreateDirectory(uploadsFolder);

            var uniqueFileName = $"{Guid.NewGuid():N}{extension}";
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await model.ProofImage.CopyToAsync(stream);
            }

            var relativePath = $"/uploads/payments/{uniqueFileName}";

            var payment = new Payment
            {
                BookingId = model.BookingId,
                Amount = FixedDownpaymentAmount,
                ProofImagePath = relativePath,
                Status = PaymentStatus.Pending,
                UploadedAt = DateTime.Now
            };

            _context.Payments.Add(payment);

            booking.RequiredDownpayment = FixedDownpaymentAmount;
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

            foreach (var admin in adminIdentityUsers.Where(u => !string.IsNullOrWhiteSpace(u.Email)))
            {
                await _emailService.SendEmailAsync(
                    admin.Email!,
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
        [Authorize(Roles = "Client")]
        public async Task<IActionResult> RequestRefund(int? bookingId)
        {
            var appUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var viewModel = new RefundRequestViewModel
            {
                BookingId = bookingId ?? 0,
                FixedRefundAmount = FixedDownpaymentAmount,
                Bookings = await GetRefundableBookingsAsync(appUserId)
            };

            return View(viewModel);
        }

        [HttpPost]
        [Authorize(Roles = "Client")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestRefund(RefundRequestViewModel model)
        {
            var appUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            model.FixedRefundAmount = FixedDownpaymentAmount;

            if (!ModelState.IsValid)
            {
                model.Bookings = await GetRefundableBookingsAsync(appUserId);
                return View(model);
            }

            var booking = await _context.Bookings
                .Include(b => b.Service)
                .FirstOrDefaultAsync(b => b.Id == model.BookingId && b.ApplicationUserId == appUserId);

            if (booking == null)
            {
                ModelState.AddModelError("", "Selected booking was not found.");
                model.Bookings = await GetRefundableBookingsAsync(appUserId);
                return View(model);
            }

            if (booking.Status == BookingStatus.Confirmed || booking.Status == BookingStatus.Completed)
            {
                ModelState.AddModelError("", "Refund request is not allowed for confirmed or completed bookings.");
                model.Bookings = await GetRefundableBookingsAsync(appUserId);
                return View(model);
            }

            var hasPendingRefund = await _context.RefundRequests
                .AnyAsync(r => r.BookingId == booking.Id && r.Status == RefundStatus.Pending);

            if (hasPendingRefund)
            {
                ModelState.AddModelError("", "You already have a pending refund request for this booking.");
                model.Bookings = await GetRefundableBookingsAsync(appUserId);
                return View(model);
            }

            var fileValidationError = await ValidateProofImageAsync(model.PaymentProofImage);

            if (!string.IsNullOrWhiteSpace(fileValidationError))
            {
                ModelState.AddModelError("", fileValidationError);
                model.Bookings = await GetRefundableBookingsAsync(appUserId);
                return View(model);
            }

            var extension = Path.GetExtension(model.PaymentProofImage.FileName).ToLowerInvariant();

            var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "refunds");
            Directory.CreateDirectory(uploadsFolder);

            var uniqueFileName = $"{Guid.NewGuid():N}{extension}";
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await model.PaymentProofImage.CopyToAsync(stream);
            }

            var relativePath = $"/uploads/refunds/{uniqueFileName}";

            var refundRequest = new RefundRequest
            {
                BookingId = booking.Id,
                Amount = FixedDownpaymentAmount,
                GCashAccountName = model.GCashAccountName.Trim(),
                GCashNumber = model.GCashNumber.Trim(),
                PaymentProofImagePath = relativePath,
                ClientReason = model.ClientReason,
                Status = RefundStatus.Pending,
                RequestedAt = DateTime.Now
            };

            _context.RefundRequests.Add(refundRequest);

            _context.BookingTimelines.Add(new BookingTimeline
            {
                BookingId = booking.Id,
                EventType = TimelineEventType.RefundRequested,
                Notes = "Client submitted a refund request.",
                CreatedAt = DateTime.Now
            });

            await _context.SaveChangesAsync();

            var admins = await _userManager.GetUsersInRoleAsync("Admin");

            foreach (var admin in admins)
            {
                await CreateNotificationAsync(
                    admin.Id,
                    "New Refund Request",
                    "A client submitted a refund request for review.",
                    "/Payments/RefundRequests");
            }

            TempData["Success"] = "Your refund request has been submitted. Please wait for admin review.";
            return RedirectToAction(nameof(MyRefundRequests));
        }

        [Authorize(Roles = "Client")]
        public async Task<IActionResult> MyRefundRequests()
        {
            var appUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var requests = await _context.RefundRequests
                .Include(r => r.Booking)
                    .ThenInclude(b => b!.Service)
                .Where(r => r.Booking != null && r.Booking.ApplicationUserId == appUserId)
                .OrderByDescending(r => r.RequestedAt)
                .ToListAsync();

            return View(requests);
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> RefundRequests()
        {
            var requests = await _context.RefundRequests
                .Include(r => r.Booking)
                    .ThenInclude(b => b!.Client)
                .Include(r => r.Booking)
                    .ThenInclude(b => b!.Service)
                .OrderByDescending(r => r.RequestedAt)
                .ToListAsync();

            return View(requests);
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveRefund(int id, string? adminRemarks)
        {
            var refund = await _context.RefundRequests
                .Include(r => r.Booking)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (refund == null) return NotFound();

            refund.Status = RefundStatus.Approved;
            refund.AdminRemarks = adminRemarks;
            refund.ProcessedAt = DateTime.Now;

            await _context.SaveChangesAsync();

            if (refund.Booking != null && !string.IsNullOrWhiteSpace(refund.Booking.ApplicationUserId))
            {
                await CreateNotificationAsync(
                    refund.Booking.ApplicationUserId,
                    "Refund Request Approved",
                    "Your refund request was approved. Please wait for the admin to process the actual GCash refund.",
                    "/Payments/MyRefundRequests");
            }

            TempData["Success"] = "Refund request approved.";
            return RedirectToAction(nameof(RefundRequests));
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAsRefunded(int id, string? adminRemarks)
        {
            var refund = await _context.RefundRequests
                .Include(r => r.Booking)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (refund == null) return NotFound();

            refund.Status = RefundStatus.Refunded;
            refund.AdminRemarks = adminRemarks;
            refund.ProcessedAt = DateTime.Now;

            await _context.SaveChangesAsync();

            if (refund.Booking != null && !string.IsNullOrWhiteSpace(refund.Booking.ApplicationUserId))
            {
                await CreateNotificationAsync(
                    refund.Booking.ApplicationUserId,
                    "Refund Processed",
                    "Your refund has been marked as processed by the admin.",
                    "/Payments/MyRefundRequests");
            }

            TempData["Success"] = "Refund marked as processed.";
            return RedirectToAction(nameof(RefundRequests));
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectRefund(int id, string? adminRemarks)
        {
            var refund = await _context.RefundRequests
                .Include(r => r.Booking)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (refund == null) return NotFound();

            refund.Status = RefundStatus.Rejected;
            refund.AdminRemarks = adminRemarks;
            refund.ProcessedAt = DateTime.Now;

            await _context.SaveChangesAsync();

            if (refund.Booking != null && !string.IsNullOrWhiteSpace(refund.Booking.ApplicationUserId))
            {
                await CreateNotificationAsync(
                    refund.Booking.ApplicationUserId,
                    "Refund Request Rejected",
                    "Your refund request was rejected. Please check the admin remarks.",
                    "/Payments/MyRefundRequests");
            }

            TempData["Success"] = "Refund request rejected.";
            return RedirectToAction(nameof(RefundRequests));
        }
        private async Task<bool> IsSlotTakenByAnotherConfirmedBookingAsync(Booking booking)
        {
            return await _context.Bookings.AnyAsync(b =>
                b.Id != booking.Id &&
                b.Status == BookingStatus.Confirmed &&
                b.EventDate.Date == booking.EventDate.Date &&
                b.StartTime < booking.EndTime &&
                booking.StartTime < b.EndTime);
        }

        private async Task<string?> ValidateProofImageAsync(IFormFile? file)
        {
            if (file == null || file.Length == 0)
                return "Please upload a proof image.";

            if (file.Length > MaxProofImageSize)
                return "File is too large. Maximum allowed size is 5MB.";

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

            if (!AllowedExtensions.Contains(extension))
                return "Only JPG, JPEG, PNG, and WEBP files are allowed.";

            if (!AllowedContentTypes.Contains(file.ContentType.ToLowerInvariant()))
                return "Invalid file type. Please upload a valid image file.";

            if (!await HasValidImageSignatureAsync(file))
                return "The uploaded file does not appear to be a valid image.";

            return null;
        }

        private async Task<bool> HasValidImageSignatureAsync(IFormFile file)
        {
            byte[] header = new byte[12];

            await using var stream = file.OpenReadStream();
            var bytesRead = await stream.ReadAsync(header, 0, header.Length);

            if (bytesRead < 4)
                return false;

            // JPG/JPEG
            if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
                return true;

            // PNG
            if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
                return true;

            // WEBP: RIFF....WEBP
            if (bytesRead >= 12 &&
                header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 &&
                header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
                return true;

            return false;
        }

        private async Task<List<SelectListItem>> GetRefundableBookingsAsync(string? appUserId)
        {
            return await _context.Bookings
                .Include(b => b.Service)
                .Where(b => b.ApplicationUserId == appUserId &&
                            b.Status != BookingStatus.Confirmed &&
                            b.Status != BookingStatus.Completed)
                .OrderByDescending(b => b.CreatedAt)
                .Select(b => new SelectListItem
                {
                    Value = b.Id.ToString(),
                    Text = "BK-" + b.CreatedAt.Year + "-" + b.Id.ToString("D3") + " - " +
                        b.Service!.Name + " - " +
                        b.EventDate.ToString("MMM dd, yyyy")
                })
                .ToListAsync();
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