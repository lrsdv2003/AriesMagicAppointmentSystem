using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
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

        public PaymentsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // CLIENT: upload payment proof
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

            var payment = new Payment
            {
                BookingId = model.BookingId,
                Amount = model.Amount,
                ProofImagePath = model.ProofImagePath,
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

            return RedirectToAction(nameof(MyUploads));
        }

        // CLIENT: view own uploaded payments
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

        // ADMIN: list pending verifications
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

        // ADMIN: confirm verify page
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

            return RedirectToAction(nameof(PendingVerification));
        }

        // ADMIN: reject confirm page
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
    }
}