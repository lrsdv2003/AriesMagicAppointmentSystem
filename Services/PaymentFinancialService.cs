using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace AriesMagicAppointmentSystem.Services
{
    public interface IPaymentFinancialService
    {
        Task<BookingFinancialSummaryViewModel> GetSummaryAsync(int bookingId);
        Task<Dictionary<int, BookingFinancialSummaryViewModel>> GetSummariesAsync(IEnumerable<int> bookingIds);
        BookingFinancialSummaryViewModel Calculate(Booking booking, IEnumerable<Payment> payments, IEnumerable<RefundRequest> refunds);
    }

    public class PaymentFinancialService : IPaymentFinancialService
    {
        private readonly ApplicationDbContext _context;

        public PaymentFinancialService(ApplicationDbContext context)
        {
            _context = context;
        }
        public async Task<BookingFinancialSummaryViewModel> GetSummaryAsync(int bookingId)
        {
            var booking = await _context.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bookingId)
                ?? throw new InvalidOperationException($"Booking {bookingId} was not found.");
            var payments = await _context.Payments.AsNoTracking().Where(p => p.BookingId == bookingId).ToListAsync();
            var refunds = await _context.RefundRequests.AsNoTracking().Where(r => r.BookingId == bookingId).ToListAsync();
            return Calculate(booking, payments, refunds);
        }

        public async Task<Dictionary<int, BookingFinancialSummaryViewModel>> GetSummariesAsync(IEnumerable<int> bookingIds)
        {
            var ids = bookingIds.Distinct().ToList();
            if (ids.Count == 0) return new();
            var bookings = await _context.Bookings.AsNoTracking().Where(b => ids.Contains(b.Id)).ToListAsync();
            var payments = await _context.Payments.AsNoTracking().Where(p => ids.Contains(p.BookingId)).ToListAsync();
            var refunds = await _context.RefundRequests.AsNoTracking().Where(r => ids.Contains(r.BookingId)).ToListAsync();
            return bookings.ToDictionary(b => b.Id, b => Calculate(b, payments.Where(p => p.BookingId == b.Id), refunds.Where(r => r.BookingId == b.Id)));
        }
        public BookingFinancialSummaryViewModel Calculate(Booking booking, IEnumerable<Payment> paymentSource, IEnumerable<RefundRequest> refundSource)
        {
            var payments = paymentSource.OrderBy(p => p.UploadedAt).ThenBy(p => p.Id).ToList();
            var refunds = refundSource.ToList();
            var bookingTotal = Math.Max(0m, booking.FinalPrice > 0 ? booking.FinalPrice : booking.BasePrice);
            var required = booking.RequiredDownpayment > 0 ? booking.RequiredDownpayment : 2000m;
            var verified = payments.Where(p => p.Status == PaymentStatus.Verified).ToList();
            var totalVerified = verified.Sum(p => p.Amount);
            var completedRefunds = refunds.Where(r => r.Status == RefundStatus.Refunded)
                .Sum(r => r.ApprovedAmount ?? r.Amount);
            var remaining = Math.Max(0m, bookingTotal - totalVerified);
            var pending = payments.Any(p => p.Status == PaymentStatus.Pending);
            var evidence = payments.Any(p => p.Status == PaymentStatus.AdditionalEvidenceRequired);
            var latest = payments.OrderByDescending(p => p.UploadedAt).ThenByDescending(p => p.Id).FirstOrDefault();

            var model = new BookingFinancialSummaryViewModel
            {
                BookingId = booking.Id,
                BookingTotal = bookingTotal,
                RequiredDownpayment = required,
                TotalVerifiedPayments = totalVerified,
                CompletedRefunds = completedRefunds,
                NetCollected = Math.Max(0m, totalVerified - completedRefunds),
                RemainingBalance = remaining,
                VerifiedPaymentCount = verified.Count,
                HasPendingVerification = pending,
                HasAdditionalEvidenceRequired = evidence,
                DownPaymentCollected = verified.FirstOrDefault()?.Amount ?? 0m
            };
            model.PaymentStatus = model.IsFullyPaid
                ? BookingPaymentState.FullyPaid
                : evidence
                    ? BookingPaymentState.AdditionalEvidenceRequired
                    : pending
                        ? BookingPaymentState.VerificationPending
                        : latest?.Status == PaymentStatus.Rejected
                            ? BookingPaymentState.PaymentRejected
                            : totalVerified > 0
                                ? (verified.Count == 1 && totalVerified >= required ? BookingPaymentState.DownPaymentPaid : BookingPaymentState.PartiallyPaid)
                                : BookingPaymentState.Unpaid;

            decimal verifiedBefore = 0m;
            foreach (var payment in payments)
            {
                var remainingBefore = Math.Max(0m, bookingTotal - verifiedBefore);
                var type = verifiedBefore <= 0m
                    ? "Down Payment"
                    : payment.Amount >= remainingBefore && remainingBefore > 0m
                        ? "Final Payment"
                        : "Additional Payment";
                model.Ledger.Add(new PaymentLedgerItemViewModel
                {
                    PaymentId = payment.Id,
                    Date = payment.VerifiedAt ?? payment.UploadedAt,
                    TransactionType = type,
                    Amount = payment.Amount,
                    Status = payment.Status,
                    PaymentMethod = payment.PaymentMethod,
                    ReferenceNumber = payment.TransactionReference
                });
                if (payment.Status == PaymentStatus.Verified) verifiedBefore += payment.Amount;
            }
            return model;
        }
    }
}
