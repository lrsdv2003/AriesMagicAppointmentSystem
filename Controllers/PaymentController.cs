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
        private readonly IOcrVerificationService _ocrService;
        private readonly ISystemActivityService _activityService;
        private readonly IConfiguration _configuration;
        private readonly IPaymentFinancialService _financialService;
        private const decimal FixedDownpaymentAmount = 2000m;

        private const long MaxProofImageSize = 5 * 1024 * 1024; // 5MB

        private static readonly string[] AllowedExtensions =
        {
            ".jpg", ".jpeg", ".png", ".webp"
        };
        private static readonly string[] AllowedContentTypes =
        {
            "image/jpeg",
            "image/png",
            "image/webp"
        };

        public PaymentsController(
            ApplicationDbContext context,
            IWebHostEnvironment environment,
            IEmailService emailService,
            UserManager<ApplicationUser> userManager,
            ContractPdfService contractPdfService,
            IOcrVerificationService ocrService,
            ISystemActivityService activityService,
            IConfiguration configuration,
            IPaymentFinancialService financialService)
        {
            _context = context;
            _environment = environment;
            _emailService = emailService;
            _userManager = userManager;
            _contractPdfService = contractPdfService;
            _ocrService = ocrService;
            _activityService = activityService;
            _configuration = configuration;
            _financialService = financialService;
        }

        public override async Task OnActionExecutionAsync(
            Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context,
            Microsoft.AspNetCore.Mvc.Filters.ActionExecutionDelegate next)
        {
            var result = await next();
            if (Request.Headers["X-Owner-Action"] != "true" || !HttpMethods.IsPost(Request.Method)) return;
            if (result.Exception != null && !result.ExceptionHandled)
            {
                HttpContext.RequestServices.GetRequiredService<ILogger<PaymentsController>>()
                    .LogError(result.Exception, "Owner financial action failed");
                result.ExceptionHandled = true;
                result.Result = StatusCode(500, new { error = "Unable to finish this action. Refresh to check the current status." });
            }
            else if (result.Result is RedirectToActionResult redirect)
            {
                result.Result = Json(new { redirectUrl = Url.Action(redirect.ActionName, redirect.ControllerName, redirect.RouteValues) });
            }
        }

        [Authorize(Roles = "Client")]
        public async Task<IActionResult> Upload()
        {
            var appUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var viewModel = new PaymentUploadViewModel
            {
                FixedDownpaymentAmount = FixedDownpaymentAmount,
                Amount = FixedDownpaymentAmount,
                GCashQrPath = "/images/gcash-qr.jpeg",
                Bookings = await GetPayableBookingsAsync(appUserId)
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
            model.GCashQrPath = "/images/gcash-qr.jpeg";

            if (!ModelState.IsValid)
            {
                model.Bookings = await GetPayableBookingsAsync(appUserId);
                return View(model);
            }

            var booking = await _context.Bookings
                .Include(b => b.Service)
                .FirstOrDefaultAsync(b => b.Id == model.BookingId && b.ApplicationUserId == appUserId);

            if (booking == null || !(booking.Status == BookingStatus.AwaitingDownpayment || booking.Status == BookingStatus.Confirmed || booking.Status == BookingStatus.Completed))
            {
                ModelState.AddModelError("", "Selected booking is not eligible for a payment.");
                model.Bookings = await GetPayableBookingsAsync(appUserId);
                return View(model);
            }

            var financialBefore = await _financialService.GetSummaryAsync(booking.Id);
            if (financialBefore.RemainingBalance <= 0)
            {
                ModelState.AddModelError("", "This booking is already fully paid.");
                model.Bookings = await GetPayableBookingsAsync(appUserId);
                return View(model);
            }
            if (financialBefore.HasPendingVerification || financialBefore.HasAdditionalEvidenceRequired)
            {
                ModelState.AddModelError("", "A payment for this booking is already awaiting verification. Please wait for the current review to finish.");
                model.Bookings = await GetPayableBookingsAsync(appUserId);
                return View(model);
            }
            if (financialBefore.TotalVerifiedPayments <= 0 && financialBefore.RemainingBalance > FixedDownpaymentAmount && model.Amount < FixedDownpaymentAmount)
            {
                ModelState.AddModelError(nameof(model.Amount), "Minimum down payment is ₱2,000.");
                model.Bookings = await GetPayableBookingsAsync(appUserId);
                return View(model);
            }
            if (financialBefore.RemainingBalance <= FixedDownpaymentAmount && model.Amount < financialBefore.RemainingBalance)
            {
                ModelState.AddModelError(nameof(model.Amount), $"The final outstanding balance is ₱{financialBefore.RemainingBalance:N2}. Please pay the exact remaining balance.");
                model.Bookings = await GetPayableBookingsAsync(appUserId);
                return View(model);
            }

            var requestedStart = booking.StartTime;
            var requestedEnd = booking.EndTime;
            if (financialBefore.TotalVerifiedPayments <= 0 && await HasPaidScheduleConflict(
                booking.Id,
                requestedStart,
                requestedEnd))
            {
                ModelState.AddModelError(
                    "",
                    "This schedule is no longer available because another client has already submitted payment for the same date and time.");

                model.Bookings = await GetPayableBookingsAsync(appUserId);

                return View(model);
            }
            if (model.ProofImage == null || model.ProofImage.Length == 0)
            {
                ModelState.AddModelError("", "Please upload a proof image.");
                model.Bookings = await GetPayableBookingsAsync(appUserId);
                return View(model);
            }

            var fileValidationError = await ValidateProofImageAsync(model.ProofImage);

            if (!string.IsNullOrWhiteSpace(fileValidationError))
            {
                ModelState.AddModelError("", fileValidationError);
                model.Bookings = await GetPayableBookingsAsync(appUserId);
                return View(model);
            }

            var savedProof = await SavePrivateProofAsync(model.ProofImage, "payments");

            var payment = new Payment
            {
                BookingId = model.BookingId,
                Amount = model.Amount,
                ProofImagePath = savedProof.StoredPath,
                PaymentMethod = model.PaymentMethod.Trim(),
                Status = PaymentStatus.Pending,
                UploadedAt = DateTime.Now
            };

            _context.Payments.Add(payment);

            booking.RequiredDownpayment = FixedDownpaymentAmount;
            if (financialBefore.TotalVerifiedPayments <= 0 && booking.Status == BookingStatus.AwaitingDownpayment)
                booking.Status = BookingStatus.AwaitingVerification;
            if (model.Amount > financialBefore.RemainingBalance)
                payment.ReviewerNote = $"Payment amount exceeds the remaining balance of ₱{financialBefore.RemainingBalance:N2}. Manual review required.";

            _context.BookingTimelines.Add(new BookingTimeline
            {
                BookingId = booking.Id,
                EventType = TimelineEventType.DownpaymentUploaded,
                Notes = "Client uploaded payment proof.",
                CreatedAt = DateTime.Now
            });

            await _context.SaveChangesAsync();

            var ocr = await AnalyzePaymentProofAsync(payment, savedProof.PhysicalPath, OcrVerificationPurposes.PaymentProof);
            _context.OcrVerifications.Add(ocr);
            payment.TransactionReference = ocr.ExtractedReferenceNumber;
            await _context.SaveChangesAsync();

            await LogActivityAsync(SystemActivityType.PaymentProofSubmitted, $"Payment proof submitted for booking #{booking.Id}.", payment.Id.ToString(), "Payment");
            await LogActivityAsync(SystemActivityType.OcrAnalysisCompleted, $"OCR analysis completed for payment #{payment.Id}: {ocr.VerificationResult}.", payment.Id.ToString(), "Payment", new { ocr.VerificationResult, ocr.OcrConfidence, ocr.IsDuplicateReference });
            if (financialBefore.TotalVerifiedPayments <= 0)
                await NotifyAffectedClientsAboutLockedScheduleAsync(booking);
            await NotifyInternalReviewersAsync("New Payment Verification Required", $"A payment proof was submitted for Booking #BK-{booking.Id}. OCR result: {ocr.VerificationResult}.", $"/Payments/Verify/{payment.Id}");

            TempData["Success"] = model.Amount > financialBefore.RemainingBalance
                ? $"Payment amount exceeds the remaining balance of ₱{financialBefore.RemainingBalance:N2}. Manual review required; the balance will not change unless an authorized reviewer resolves the discrepancy."
                : "Payment proof successfully submitted. Your payment is awaiting Owner verification. Your balance will update only after Owner approval.";
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

            ViewBag.FinancialSummaries = await _financialService.GetSummariesAsync(payments.Select(p => p.BookingId));
            return View(payments);
        }

        [Authorize]
        public async Task<IActionResult> PaymentProof(int id)
        {
            var payment = await _context.Payments.AsNoTracking().Include(p => p.Booking).FirstOrDefaultAsync(p => p.Id == id);
            if (payment == null) return NotFound();
            var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var internalUser = User.IsInRole("Admin") || User.IsInRole("Owner");
            if (!internalUser && (!User.IsInRole("Client") || payment.Booking?.ApplicationUserId != uid)) return Forbid();
            return ServeFinancialProof(payment.ProofImagePath);
        }

        [Authorize]
        public async Task<IActionResult> RefundProof(int id, bool confirmation = false)
        {
            var refund = await _context.RefundRequests.AsNoTracking().Include(r => r.Booking).FirstOrDefaultAsync(r => r.Id == id);
            if (refund == null) return NotFound();
            var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var internalUser = User.IsInRole("Admin") || User.IsInRole("Owner");
            if (!internalUser && (!User.IsInRole("Client") || refund.Booking?.ApplicationUserId != uid)) return Forbid();
            var path = confirmation ? refund.RefundProofImagePath : refund.PaymentProofImagePath;
            if (string.IsNullOrWhiteSpace(path)) return NotFound();
            return ServeFinancialProof(path);
        }

        [HttpPost]
        [Authorize(Roles = "Owner")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestAdditionalEvidence(int id, string? reason)
        {
            var payment = await _context.Payments.Include(p => p.Booking).FirstOrDefaultAsync(p => p.Id == id);
            if (payment == null) return NotFound();
            if (payment.Status is PaymentStatus.Verified or PaymentStatus.Rejected)
                return RedirectToAction(nameof(Verify), new { id });
            var previousStatus = payment.Status;
            payment.Status = PaymentStatus.AdditionalEvidenceRequired;
            payment.ReviewerNote = string.IsNullOrWhiteSpace(reason) ? "Please upload a clearer screenshot showing the full transaction details." : reason.Trim();
            payment.VerifiedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            payment.VerifiedByUserName = User.Identity?.Name;
            await _context.SaveChangesAsync();
            if (!string.IsNullOrWhiteSpace(payment.Booking?.ApplicationUserId))
                await CreateNotificationAsync(payment.Booking.ApplicationUserId, "Additional Payment Proof Required", "We were unable to fully verify your payment proof. Please upload a clearer screenshot or provide additional payment information.", "/Payments/MyUploads");
            await LogActivityAsync(SystemActivityType.PaymentAdditionalEvidenceRequested, $"Owner requested additional evidence for payment #{id}.", id.ToString(), "Payment", new { PreviousStatus = previousStatus, NewStatus = payment.Status, OwnerUserId = payment.VerifiedByUserId, OwnerName = payment.VerifiedByUserName, ReviewNotes = payment.ReviewerNote });
            TempData["Success"] = "Client was asked to submit additional payment evidence.";
            return RedirectToAction(nameof(Verify), new { id });
        }

        [HttpPost]
        [Authorize(Roles = "Client")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReplaceProof(int id, IFormFile proofImage)
        {
            var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var payment = await _context.Payments.Include(p => p.Booking).FirstOrDefaultAsync(p => p.Id == id && p.Booking != null && p.Booking.ApplicationUserId == uid);
            if (payment == null) return NotFound();
            if (payment.Status != PaymentStatus.AdditionalEvidenceRequired) return Forbid();
            var validation = await ValidateProofImageAsync(proofImage);
            if (validation != null) { TempData["Error"] = validation; return RedirectToAction(nameof(MyUploads)); }
            var saved = await SavePrivateProofAsync(proofImage, "payments");
            payment.ProofImagePath = saved.StoredPath;
            payment.Status = PaymentStatus.Pending;
            payment.UploadedAt = DateTime.Now;
            payment.ReviewerNote = null;
            payment.TransactionReference = null;
            await _context.SaveChangesAsync();
            var ocr = await AnalyzePaymentProofAsync(payment, saved.PhysicalPath);
            _context.OcrVerifications.Add(ocr);
            payment.TransactionReference = ocr.ExtractedReferenceNumber;
            await _context.SaveChangesAsync();
            await NotifyInternalReviewersAsync("Updated Payment Proof Submitted", $"Replacement evidence for Booking #BK-{payment.BookingId}. OCR result: {ocr.VerificationResult}.", $"/Payments/Verify/{payment.Id}");
            await LogActivityAsync(SystemActivityType.PaymentProofSubmitted, $"Replacement payment proof submitted for payment #{id}.", id.ToString(), "Payment");
            TempData["Success"] = "Your new payment proof was submitted and is awaiting Owner verification.";
            return RedirectToAction(nameof(MyUploads));
        }

        [Authorize(Roles = "Admin,Owner")]
        public async Task<IActionResult> PendingVerification(string filter = "awaiting", string? q = null, string? paymentMethod = null, DateTime? date = null)
        {
            var query = _context.Payments
                .AsNoTracking()
                .Include(p => p.Booking).ThenInclude(b => b!.Client)
                .Include(p => p.Booking).ThenInclude(b => b!.Service)
                .Include(p => p.OcrVerifications)
                .AsQueryable();

            query = filter.ToLowerInvariant() switch
            {
                "verified" => query.Where(p => p.Status == PaymentStatus.Verified),
                "rejected" => query.Where(p => p.Status == PaymentStatus.Rejected),
                "evidence" => query.Where(p => p.Status == PaymentStatus.AdditionalEvidenceRequired),
                "mismatch" => query.Where(p => p.OcrVerifications.Where(o => o.VerificationPurpose == OcrVerificationPurposes.PaymentProof).OrderByDescending(o => o.ProcessedAt).Select(o => o.VerificationResult).FirstOrDefault() == OcrVerificationResults.MismatchDetected),
                "ocrfailed" => query.Where(p => p.OcrVerifications.Where(o => o.VerificationPurpose == OcrVerificationPurposes.PaymentProof).OrderByDescending(o => o.ProcessedAt).Select(o => o.VerificationResult).FirstOrDefault() == OcrVerificationResults.OcrFailed),
                _ => query.Where(p => p.Status == PaymentStatus.Pending)
            };

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                query = query.Where(p => p.Id.ToString().Contains(term) || p.BookingId.ToString().Contains(term) ||
                    (p.Booking!.Client!.FullName.Contains(term)) ||
                    (p.TransactionReference != null && p.TransactionReference.Contains(term)));
            }
            if (!string.IsNullOrWhiteSpace(paymentMethod)) query = query.Where(p => p.PaymentMethod == paymentMethod);
            if (date.HasValue) query = query.Where(p => p.UploadedAt.Date == date.Value.Date);

            ViewBag.Filter = filter; ViewBag.Search = q; ViewBag.PaymentMethod = paymentMethod; ViewBag.Date = date?.ToString("yyyy-MM-dd");
            var payments = await query.OrderByDescending(p => p.UploadedAt).Take(250).ToListAsync();
            return View(payments);
        }

        [Authorize(Roles = "Admin,Owner")]
        public async Task<IActionResult> Verify(int? id)
        {
            if (id == null) return NotFound();

            var payment = await _context.Payments
                .Include(p => p.Booking)
                    .ThenInclude(b => b!.Client)
                .Include(p => p.Booking)
                    .ThenInclude(b => b!.Service)
                .Include(p => p.OcrVerifications)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (payment == null) return NotFound();
            ViewBag.FinancialSummary = await _financialService.GetSummaryAsync(payment.BookingId);
            ViewBag.ExpectedReceiver = _configuration["OcrVerification:ExpectedReceiver"] ?? "Aries Magic";
            return View(payment);
        }

        [HttpPost, ActionName("Verify")]
        [Authorize(Roles = "Owner")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyConfirmed(int id)
        {
            var payment = await _context.Payments
                .Include(p => p.Booking)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (payment == null) return NotFound();

            if (payment.Status != PaymentStatus.Pending)
            {
                TempData["Error"] = "Only pending payments can be verified.";
                return RedirectToAction(nameof(PendingVerification));
            }

            if (payment.Amount <= 0m)
            {
                TempData["Error"] = "The submitted payment amount must be greater than zero.";
                return RedirectToAction(nameof(Verify), new { id });
            }
            var financialBefore = await _financialService.GetSummaryAsync(payment.BookingId);
            var latestOcr = await _context.OcrVerifications.AsNoTracking()
                .Where(o => o.PaymentId == payment.Id && o.VerificationPurpose == OcrVerificationPurposes.PaymentProof)
                .OrderByDescending(o => o.ProcessedAt).FirstOrDefaultAsync();
            var detectedAmount = latestOcr?.ExtractedAmount;
            if (payment.Amount > financialBefore.RemainingBalance || (detectedAmount.HasValue && detectedAmount.Value > financialBefore.RemainingBalance))
            {
                TempData["Error"] = $"Payment amount exceeds the remaining balance of ₱{financialBefore.RemainingBalance:N2}. Manual review required. Do not approve this payment until the discrepancy is resolved.";
                return RedirectToAction(nameof(Verify), new { id });
            }
            if (financialBefore.TotalVerifiedPayments <= 0 && financialBefore.RemainingBalance > FixedDownpaymentAmount && payment.Amount < FixedDownpaymentAmount)
            {
                TempData["Error"] = "Minimum down payment is ₱2,000.";
                return RedirectToAction(nameof(Verify), new { id });
            }

            var previousStatus = payment.Status;
            payment.Status = PaymentStatus.Verified;
            payment.VerifiedAt = DateTime.Now;
            payment.VerifiedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            payment.VerifiedByUserName = User.Identity?.Name;

            if (payment.Booking != null)
            {
                if (financialBefore.TotalVerifiedPayments <= 0 && payment.Booking.Status == BookingStatus.AwaitingVerification)
                    payment.Booking.Status = BookingStatus.Confirmed;

                _context.BookingTimelines.Add(new BookingTimeline
                {
                    BookingId = payment.Booking.Id,
                    EventType = TimelineEventType.PaymentVerified,
                    Notes = "Authorized reviewer verified payment proof after OCR-assisted review.",
                    CreatedAt = DateTime.Now
                });

                if (financialBefore.TotalVerifiedPayments <= 0)
                {
                    _context.BookingTimelines.Add(new BookingTimeline
                    {
                        BookingId = payment.Booking.Id,
                        EventType = TimelineEventType.BookingConfirmed,
                        Notes = "Booking confirmed after manually verified downpayment.",
                        CreatedAt = DateTime.Now
                    });
                }
            }

            await _context.SaveChangesAsync();

            var financialAfter = await _financialService.GetSummaryAsync(payment.BookingId);
            if (payment.Booking != null && !string.IsNullOrWhiteSpace(payment.Booking.ApplicationUserId))
            {
                var notificationTitle = financialAfter.IsFullyPaid
                    ? "Payment Complete"
                    : financialBefore.TotalVerifiedPayments <= 0 ? "Down Payment Verified" : "Payment Verified";
                var notificationMessage = financialAfter.IsFullyPaid
                    ? "Your booking is now fully paid."
                    : $"Your payment of ₱{payment.Amount:N2} has been verified. Remaining Balance: ₱{financialAfter.RemainingBalance:N2}.";
                await CreateNotificationAsync(payment.Booking.ApplicationUserId, notificationTitle, notificationMessage, "/Payments/MyUploads");
            }

            if (payment.Booking != null && financialBefore.TotalVerifiedPayments <= 0)
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

            await LogActivityAsync(SystemActivityType.PaymentVerified, $"Payment #{payment.Id} approved by Owner after OCR-assisted review.", payment.Id.ToString(), "Payment", new { PreviousStatus = previousStatus, NewStatus = payment.Status, OwnerUserId = payment.VerifiedByUserId, OwnerName = payment.VerifiedByUserName, ReviewNotes = payment.ReviewerNote, payment.Amount });
            return RedirectToAction(nameof(PendingVerification));
        }
        [HttpPost, ActionName("Reject")]
        [Authorize(Roles = "Owner")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectConfirmed(int id, string? rejectionReason)
        {
            if (string.IsNullOrWhiteSpace(rejectionReason) || rejectionReason.Trim().Length > 1000)
            {
                TempData["Error"] = "Enter a rejection reason between 1 and 1,000 characters.";
                return RedirectToAction(nameof(Verify), new { id });
            }
            rejectionReason = rejectionReason.Trim();

            var payment = await _context.Payments
                .Include(p => p.Booking)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (payment == null) return NotFound();

            if (payment.Status != PaymentStatus.Pending)
            {
                TempData["Error"] = "Only pending payments can be rejected.";
                return RedirectToAction(nameof(PendingVerification));
            }

            var previousStatus = payment.Status;
            payment.Status = PaymentStatus.Rejected;
            payment.RejectionReason = rejectionReason;
            payment.ReviewerNote = rejectionReason;
            payment.VerifiedAt = DateTime.Now;
            payment.VerifiedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            payment.VerifiedByUserName = User.Identity?.Name;

            var rejectionFinancial = await _financialService.GetSummaryAsync(payment.BookingId);
            if (payment.Booking != null && rejectionFinancial.TotalVerifiedPayments <= 0 && payment.Booking.Status == BookingStatus.AwaitingVerification)
                payment.Booking.Status = BookingStatus.AwaitingDownpayment;

            await _context.SaveChangesAsync();

            if (payment.Booking != null && !string.IsNullOrWhiteSpace(payment.Booking.ApplicationUserId))
            {
                await CreateNotificationAsync(
                    payment.Booking.ApplicationUserId,
                    rejectionFinancial.TotalVerifiedPayments <= 0 ? "Payment Rejected" : "Additional Payment Rejected",
                    rejectionFinancial.TotalVerifiedPayments <= 0
                        ? "Your payment proof was rejected. Please upload a new downpayment proof."
                        : $"Your additional payment proof was rejected. Your previously verified payments remain unchanged. Remaining Balance: ₱{rejectionFinancial.RemainingBalance:N2}.",
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
            await LogActivityAsync(SystemActivityType.PaymentRejected, $"Payment #{payment.Id} rejected by Owner after manual review.", payment.Id.ToString(), "Payment", new { PreviousStatus = previousStatus, NewStatus = payment.Status, OwnerUserId = payment.VerifiedByUserId, OwnerName = payment.VerifiedByUserName, ReviewNotes = rejectionReason, payment.Amount });
            TempData["Success"] = rejectionFinancial.TotalVerifiedPayments <= 0 ? "Payment rejected and booking returned to downpayment." : "Additional payment rejected. Existing verified balance was not changed.";
            return RedirectToAction(nameof(PendingVerification));
        }

        [Authorize(Roles = "Client")]
        public async Task<IActionResult> RequestRefund(int? bookingId)
        {
            var appUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var model = new RefundRequestViewModel
            {
                BookingId = bookingId ?? 0,
                FixedRefundAmount = FixedDownpaymentAmount,
                Bookings = await GetRefundableBookingsAsync(appUserId)
            };

            return View(model);
        }

        [HttpPost]
        [Authorize(Roles = "Client")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestRefund(RefundRequestViewModel model)
        {
            var appUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            model.FixedRefundAmount = FixedDownpaymentAmount;
            model.Bookings = await GetRefundableBookingsAsync(appUserId);

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var booking = await _context.Bookings
                .Include(b => b.Service)
                .FirstOrDefaultAsync(b =>
                    b.Id == model.BookingId &&
                    b.ApplicationUserId == appUserId);

            if (booking == null)
            {
                ModelState.AddModelError("", "Selected booking was not found.");
                return View(model);
            }

            if (booking.Status != BookingStatus.AwaitingVerification)
            {
                ModelState.AddModelError("", "Refund request is only allowed for bookings that are awaiting payment verification.");
                return View(model);
            }

            var existingActiveRefund = await _context.RefundRequests
                .AnyAsync(r =>
                    r.BookingId == booking.Id &&
                    (r.Status == RefundStatus.Pending || r.Status == RefundStatus.UnderReview || r.Status == RefundStatus.Approved || r.Status == RefundStatus.RefundProcessing));

            if (existingActiveRefund)
            {
                ModelState.AddModelError("", "You already have an active refund request for this booking.");
                return View(model);
            }

            var originalPayment = await _context.Payments
                .Where(p => p.BookingId == booking.Id && p.Status != PaymentStatus.Rejected)
                .OrderByDescending(p => p.UploadedAt)
                .FirstOrDefaultAsync();
            if (originalPayment == null)
            {
                ModelState.AddModelError("", "A payment record is required before a refund request can be submitted.");
                return View(model);
            }

            var fileValidationError = await ValidateProofImageAsync(model.PaymentProofImage);

            if (!string.IsNullOrWhiteSpace(fileValidationError))
            {
                ModelState.AddModelError("PaymentProofImage", fileValidationError);
                return View(model);
            }

            var savedRefundProof = await SavePrivateProofAsync(model.PaymentProofImage!, "refunds");

            var refundRequest = new RefundRequest
            {
                BookingId = booking.Id,
                OriginalPaymentId = originalPayment.Id,
                OriginalPayment = originalPayment,
                Amount = Math.Min(FixedDownpaymentAmount, originalPayment.Amount),
                GCashAccountName = model.GCashAccountName.Trim(),
                GCashNumber = model.GCashNumber.Trim(),
                PaymentProofImagePath = savedRefundProof.StoredPath,
                ClientReason = string.IsNullOrWhiteSpace(model.ClientReason)
                    ? "Client requested a refund for a sent downpayment."
                    : model.ClientReason.Trim(),
                Status = RefundStatus.UnderReview,
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
            var refundOcr = await _ocrService.AnalyzeAsync(new OcrAnalysisRequest
            {
                PaymentId = originalPayment.Id, RefundRequestId = refundRequest.Id, VerificationPurpose = OcrVerificationPurposes.RefundSupport,
                FilePath = savedRefundProof.PhysicalPath, ExpectedAmount = originalPayment.Amount, ExpectedPaymentMethod = originalPayment.PaymentMethod,
                ExpectedReceiver = _configuration["OcrVerification:ExpectedReceiver"] ?? "Aries Magic", SubmittedAt = refundRequest.RequestedAt
            });
            _context.OcrVerifications.Add(refundOcr);
            await _context.SaveChangesAsync();
            await NotifyInternalReviewersAsync("New Refund Request", $"Refund request RF-{refundRequest.Id:D4} for Booking #BK-{booking.Id} requires review. OCR result: {refundOcr.VerificationResult}.", $"/Payments/RefundReview/{refundRequest.Id}");
            await LogActivityAsync(SystemActivityType.RefundRequested, $"Refund request #{refundRequest.Id} submitted for booking #{booking.Id}.", refundRequest.Id.ToString(), "RefundRequest", new { refundOcr.VerificationResult });
            TempData["Success"] = "Your refund request has been submitted and is awaiting Owner review.";
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

        [Authorize(Roles = "Admin,Owner")]
        public async Task<IActionResult> RefundRequests(string filter = "all", string? q = null)
        {
            var query = _context.RefundRequests.AsNoTracking()
                .Include(r => r.Booking).ThenInclude(b => b!.Client)
                .Include(r => r.Booking).ThenInclude(b => b!.Service)
                .Include(r => r.OriginalPayment).Include(r => r.OcrVerifications).AsQueryable();
            if (filter == RefundStatus.UnderReview || filter == RefundStatus.Pending)
                query = query.Where(r => r.Status == RefundStatus.UnderReview || r.Status == RefundStatus.Pending);
            else if (!string.IsNullOrWhiteSpace(filter) && filter != "all")
                query = query.Where(r => r.Status == filter);
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                query = query.Where(r => r.Id.ToString().Contains(term) || r.BookingId.ToString().Contains(term) || r.Booking!.Client!.FullName.Contains(term) || (r.OriginalPayment != null && r.OriginalPayment.TransactionReference != null && r.OriginalPayment.TransactionReference.Contains(term)));
            }
            ViewBag.Filter = filter; ViewBag.Search = q;
            return View(await query.OrderByDescending(r => r.RequestedAt).Take(250).ToListAsync());
        }

        [Authorize(Roles = "Admin,Owner")]
        public async Task<IActionResult> RefundReview(int id)
        {
            var refund = await _context.RefundRequests.Include(r => r.Booking).ThenInclude(b => b!.Client).Include(r => r.Booking).ThenInclude(b => b!.Service).Include(r => r.OriginalPayment).Include(r => r.OcrVerifications).FirstOrDefaultAsync(r => r.Id == id);
            if (refund == null) return NotFound();
            ViewBag.FinancialSummary = await _financialService.GetSummaryAsync(refund.BookingId);
            return View(refund);
        }

        [HttpPost]
        [Authorize(Roles = "Owner")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveRefund(int id)
        {
            var refund = await _context.RefundRequests
                .Include(r => r.Booking)
                .Include(r => r.OriginalPayment)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (refund == null) return NotFound();

            if (refund.Status != RefundStatus.Pending && refund.Status != RefundStatus.UnderReview)
            {
                TempData["Error"] = "Only refund requests under review can be approved.";
                return RedirectToAction(nameof(RefundRequests));
            }

            if (refund.OriginalPayment == null || refund.OriginalPayment.Status != PaymentStatus.Verified || refund.Amount <= 0)
            {
                TempData["Error"] = "A refund requires a verified original payment and a positive amount.";
                return RedirectToAction(nameof(RefundReview), new { id });
            }
            var previousStatus = refund.Status;
            refund.Status = RefundStatus.Approved;
            refund.ApprovedAmount = Math.Min(refund.Amount, refund.OriginalPayment?.Amount ?? refund.Amount);
            refund.ProcessedAt = DateTime.Now;
            refund.ReviewedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            refund.ReviewedByUserName = User.Identity?.Name;

            await _context.SaveChangesAsync();

            if (refund.Booking != null && !string.IsNullOrWhiteSpace(refund.Booking.ApplicationUserId))
            {
                await CreateNotificationAsync(
                    refund.Booking.ApplicationUserId,
                    "Refund Request Approved",
                    "Your refund request was approved. Please wait for the owner to process the actual GCash refund.",
                    "/Payments/MyRefundRequests");
            }

            await LogActivityAsync(SystemActivityType.RefundApproved, $"Refund #{refund.Id} approved by Owner.", refund.Id.ToString(), "RefundRequest", new { PreviousStatus = previousStatus, NewStatus = refund.Status, OwnerUserId = refund.ReviewedByUserId, OwnerName = refund.ReviewedByUserName, ReviewNotes = refund.AdminRemarks, ApprovedAmount = refund.ApprovedAmount });
            TempData["Success"] = "Refund request approved by Owner.";
            return RedirectToAction(nameof(RefundRequests));
        }

        [HttpPost]
        [Authorize(Roles = "Owner")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadRefundProof(int id, IFormFile refundProofImage)
        {
            var refund = await _context.RefundRequests
                .Include(r => r.Booking)
                .Include(r => r.OriginalPayment)
                .FirstOrDefaultAsync(r => r.Id == id);
            if (refund == null) return NotFound();
            if (refund.Status != RefundStatus.Approved && refund.Status != RefundStatus.RefundProcessing)
            {
                TempData["Error"] = "Refund confirmation proof can only be uploaded after approval.";
                return RedirectToAction(nameof(RefundReview), new { id });
            }
            var validation = await ValidateProofImageAsync(refundProofImage);
            if (validation != null)
            {
                TempData["Error"] = validation;
                return RedirectToAction(nameof(RefundReview), new { id });
            }
            var saved = await SavePrivateProofAsync(refundProofImage, "refund-confirmations");
            var previousStatus = refund.Status;
            refund.RefundProofImagePath = saved.StoredPath;
            refund.Status = RefundStatus.RefundProcessing;
            refund.ReviewedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            refund.ReviewedByUserName = User.Identity?.Name;
            await _context.SaveChangesAsync();
            await AnalyzeRefundProofAsync(refund, saved.PhysicalPath, OcrVerificationPurposes.RefundConfirmation);
            await LogActivityAsync(SystemActivityType.OcrAnalysisCompleted, $"Refund confirmation OCR completed for refund #{refund.Id} by Owner.", refund.Id.ToString(), "RefundRequest", new { PreviousStatus = previousStatus, NewStatus = refund.Status, OwnerUserId = refund.ReviewedByUserId, OwnerName = refund.ReviewedByUserName, ReviewNotes = refund.AdminRemarks });
            TempData["Success"] = "Refund proof uploaded and analyzed. Review the OCR result before completing the refund.";
            return RedirectToAction(nameof(RefundReview), new { id });
        }

        [HttpPost]
        [Authorize(Roles = "Owner")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAsRefunded(int id)
        {
            var refund = await _context.RefundRequests
                .Include(r => r.Booking)
                .Include(r => r.OcrVerifications)
                .FirstOrDefaultAsync(r => r.Id == id);
            if (refund == null) return NotFound();
            if (refund.Status != RefundStatus.RefundProcessing || string.IsNullOrWhiteSpace(refund.RefundProofImagePath))
            {
                TempData["Error"] = "Upload refund confirmation proof before completing the refund.";
                return RedirectToAction(nameof(RefundReview), new { id });
            }
            if (!refund.OcrVerifications.Any(o => o.VerificationPurpose == OcrVerificationPurposes.RefundConfirmation))
            {
                TempData["Error"] = "Refund confirmation OCR analysis is required before completion.";
                return RedirectToAction(nameof(RefundReview), new { id });
            }
            var previousStatus = refund.Status;
            refund.Status = RefundStatus.Refunded;
            refund.ProcessedAt = DateTime.Now;
            refund.RefundCompletedAt = DateTime.Now;
            refund.ReviewedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            refund.ReviewedByUserName = User.Identity?.Name;
            await _context.SaveChangesAsync();
            if (refund.Booking != null && !string.IsNullOrWhiteSpace(refund.Booking.ApplicationUserId))
                await CreateNotificationAsync(refund.Booking.ApplicationUserId, "Refund Completed", "Your refund has been completed after manual verification of the refund proof.", "/Payments/MyRefundRequests");
            await LogActivityAsync(SystemActivityType.RefundProcessed, $"Refund #{refund.Id} completed by Owner after manual OCR-assisted verification.", refund.Id.ToString(), "RefundRequest", new { PreviousStatus = previousStatus, NewStatus = refund.Status, OwnerUserId = refund.ReviewedByUserId, OwnerName = refund.ReviewedByUserName, ReviewNotes = refund.AdminRemarks, CompletedAmount = refund.ApprovedAmount ?? refund.Amount });
            TempData["Success"] = "Refund marked as completed.";
            return RedirectToAction(nameof(RefundReview), new { id });
        }

        [HttpPost]
        [Authorize(Roles = "Owner")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectRefund(int id, string? rejectionReason)
        {
            if (string.IsNullOrWhiteSpace(rejectionReason) || rejectionReason.Trim().Length > 1000)
            {
                TempData["Error"] = "Enter a rejection reason between 1 and 1,000 characters.";
                return RedirectToAction(nameof(RefundReview), new { id });
            }
            rejectionReason = rejectionReason.Trim();

            var refund = await _context.RefundRequests
                .Include(r => r.Booking)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (refund == null) return NotFound();

            if (refund.Status != RefundStatus.Pending && refund.Status != RefundStatus.UnderReview)
            {
                TempData["Error"] = "Only refund requests under review can be rejected.";
                return RedirectToAction(nameof(RefundRequests));
            }

            var previousStatus = refund.Status;
            refund.Status = RefundStatus.Rejected;
            refund.AdminRemarks = string.IsNullOrWhiteSpace(rejectionReason) ? null : rejectionReason.Trim();
            refund.ProcessedAt = DateTime.Now;
            refund.ReviewedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            refund.ReviewedByUserName = User.Identity?.Name;

            await _context.SaveChangesAsync();

            if (refund.Booking != null && !string.IsNullOrWhiteSpace(refund.Booking.ApplicationUserId))
            {
                await CreateNotificationAsync(
                    refund.Booking.ApplicationUserId,
                    "Refund Request Rejected",
                    string.IsNullOrWhiteSpace(rejectionReason) ? "Your refund request was rejected." : $"Your refund request was rejected. Reason: {rejectionReason}",
                    "/Payments/MyRefundRequests");
            }

            await LogActivityAsync(SystemActivityType.RefundRejected, $"Refund #{refund.Id} rejected by Owner.", refund.Id.ToString(), "RefundRequest", new { PreviousStatus = previousStatus, NewStatus = refund.Status, OwnerUserId = refund.ReviewedByUserId, OwnerName = refund.ReviewedByUserName, ReviewNotes = refund.AdminRemarks });
            TempData["Success"] = "Refund request rejected by Owner.";
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
            if (string.IsNullOrWhiteSpace(appUserId))
            {
                return new List<SelectListItem>();
            }

            return await _context.Bookings
                .Include(b => b.Service)
                .Where(b =>
                    b.ApplicationUserId == appUserId &&
                    b.Status == BookingStatus.AwaitingVerification)
                .OrderByDescending(b => b.CreatedAt)
                .Select(b => new SelectListItem
                {
                    Value = b.Id.ToString(),
                    Text = "BK-" + b.CreatedAt.Year + "-" + b.Id.ToString("D3") + " - " +
                        (b.PackageName ?? b.Service!.Name) + " - " +
                        b.EventDate.ToString("MMM dd, yyyy") + " - " +
                        b.Status
                })
                .ToListAsync();
        }

        private async Task<List<SelectListItem>> GetPayableBookingsAsync(string? appUserId)
        {
            if (string.IsNullOrWhiteSpace(appUserId)) return new();
            var candidates = await _context.Bookings.AsNoTracking()
                .Include(b => b.Service)
                .Where(b => b.ApplicationUserId == appUserId &&
                    (b.Status == BookingStatus.AwaitingDownpayment || b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Completed))
                .OrderByDescending(b => b.CreatedAt).ToListAsync();
            var summaries = await _financialService.GetSummariesAsync(candidates.Select(b => b.Id));
            return candidates.Where(b => summaries.TryGetValue(b.Id, out var f) && f.RemainingBalance > 0 && !f.HasPendingVerification && !f.HasAdditionalEvidenceRequired)
                .Select(b => new SelectListItem
                {
                    Value = b.Id.ToString(),
                    Text = $"BK-{b.CreatedAt.Year}-{b.Id:D3} - {(b.PackageName ?? b.Service?.Name ?? "Booking")} - Balance ₱{summaries[b.Id].RemainingBalance:N2}"
                }).ToList();
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
        private async Task<(string StoredPath, string PhysicalPath)> SavePrivateProofAsync(IFormFile file, string category)
        {
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            var root = Path.Combine(_environment.ContentRootPath, "App_Data", "financial-proofs", category);
            Directory.CreateDirectory(root);
            var name = $"{Guid.NewGuid():N}{extension}";
            var fullPath = Path.Combine(root, name);
            await using var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await file.CopyToAsync(stream);
            return ($"{category}/{name}", fullPath);
        }

        private Task<OcrVerification> AnalyzePaymentProofAsync(Payment payment, string fullPath, string purpose = OcrVerificationPurposes.PaymentProof)
        {
            return _ocrService.AnalyzeAsync(new OcrAnalysisRequest
            {
                PaymentId = payment.Id,
                VerificationPurpose = purpose,
                FilePath = fullPath,
                ExpectedAmount = payment.Amount,
                ExpectedPaymentMethod = payment.PaymentMethod,
                ExpectedReceiver = _configuration["OcrVerification:ExpectedReceiver"] ?? "Aries Magic",
                SubmittedAt = payment.UploadedAt
            });
        }

        private async Task AnalyzeRefundProofAsync(RefundRequest refund, string fullPath, string purpose)
        {
            var expected = purpose == OcrVerificationPurposes.RefundConfirmation ? refund.ApprovedAmount ?? refund.Amount : refund.OriginalPayment?.Amount ?? refund.Amount;
            var result = await _ocrService.AnalyzeAsync(new OcrAnalysisRequest
            {
                PaymentId = refund.OriginalPaymentId,
                RefundRequestId = refund.Id,
                VerificationPurpose = purpose,
                FilePath = fullPath,
                ExpectedAmount = expected,
                ExpectedPaymentMethod = refund.OriginalPayment?.PaymentMethod ?? "GCash",
                ExpectedReceiver = purpose == OcrVerificationPurposes.RefundConfirmation ? refund.GCashAccountName : (_configuration["OcrVerification:ExpectedReceiver"] ?? "Aries Magic"),
                SubmittedAt = DateTime.Now
            });
            if (purpose == OcrVerificationPurposes.RefundConfirmation) refund.RefundReferenceNumber ??= result.ExtractedReferenceNumber;
            _context.OcrVerifications.Add(result);
            await _context.SaveChangesAsync();
        }

        private async Task LogActivityAsync(SystemActivityType type, string description, string affectedId, string affectedType, object? metadata = null)
        {
            var uid = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "system";
            await _activityService.LogAsync(type, description, uid, User.Identity?.Name, affectedId, affectedType, metadata);
        }

        private async Task NotifyInternalReviewersAsync(string title, string message, string link)
        {
            var ids = new HashSet<string>();
            foreach (var role in new[] { "Owner" })
                foreach (var user in await _userManager.GetUsersInRoleAsync(role))
                    if (user.IsActive) ids.Add(user.Id);
            foreach (var id in ids)
                _context.Notifications.Add(new Notification { UserId = id, Title = title, Message = message, Link = link, IsRead = false, CreatedAt = DateTime.Now });
            await _context.SaveChangesAsync();
        }

        private string ResolvePrivateProofPath(string relativePath)
        {
            var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
            var root = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, "App_Data", "financial-proofs"));
            var full = Path.GetFullPath(Path.Combine(root, normalized));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid proof path.");
            return full;
        }

        private IActionResult ServeFinancialProof(string storedPath)
        {
            string fullPath;
            if (storedPath.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
            {
                var relative = storedPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                var root = Path.GetFullPath(_environment.WebRootPath);
                fullPath = Path.GetFullPath(Path.Combine(root, relative));
                if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return Forbid();
            }
            else
            {
                fullPath = ResolvePrivateProofPath(storedPath);
            }
            if (!System.IO.File.Exists(fullPath)) return NotFound();
            var contentType = Path.GetExtension(fullPath).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".webp" => "image/webp",
                _ => "image/jpeg"
            };
            Response.Headers.CacheControl = "private, no-store, max-age=0";
            Response.Headers.Pragma = "no-cache";
            return PhysicalFile(fullPath, contentType, enableRangeProcessing: true);
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
