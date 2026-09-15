using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace AriesMagicAppointmentSystem.Controllers
{
    [Authorize(Roles = "Owner")]
    public class ReportsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IPaymentFinancialService _financialService;

        public ReportsController(ApplicationDbContext context, IPaymentFinancialService financialService)
        {
            _context = context;
            _financialService = financialService;
        }

        public async Task<IActionResult> Index(DateTime? startDate, DateTime? endDate)
        {
            if (startDate.HasValue && endDate.HasValue &&
                startDate.Value.Date > endDate.Value.Date)
            {
                ModelState.AddModelError(
                    string.Empty,
                    "The start date cannot be later than the end date.");
            }

            var bookingQuery = _context.Bookings
                .Include(b => b.Client)
                .Include(b => b.Service)
                .AsNoTracking()
                .AsQueryable();

            var paymentQuery = _context.Payments
                .Include(p => p.Booking)
                .AsNoTracking()
                .AsQueryable();

            if (startDate.HasValue)
            {
                var start = startDate.Value.Date;
                bookingQuery = bookingQuery.Where(b => b.EventDate >= start);
                paymentQuery = paymentQuery.Where(p => (p.VerifiedAt ?? p.UploadedAt) >= start);
            }

            if (endDate.HasValue)
            {
                var endExclusive = endDate.Value.Date.AddDays(1);
                bookingQuery = bookingQuery.Where(b => b.EventDate < endExclusive);
                paymentQuery = paymentQuery.Where(p => (p.VerifiedAt ?? p.UploadedAt) < endExclusive);
            }

            var bookings = await bookingQuery
                .OrderByDescending(b => b.EventDate)
                .ThenByDescending(b => b.StartTime)
                .ToListAsync();

            var payments = await paymentQuery.ToListAsync();
            var verifiedPayments = payments
                .Where(p => p.Status == PaymentStatus.Verified)
                .ToList();

            var financials = await _financialService.GetSummariesAsync(bookings.Select(b => b.Id));
            var reportBookings = bookings.Where(b => b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.Declined && b.Status != BookingStatus.Expired).ToList();
            var reportFinancials = reportBookings.Where(b => financials.ContainsKey(b.Id)).Select(b => financials[b.Id]).ToList();
            var completedBookings = reportBookings.Where(b => b.Status == BookingStatus.Completed).ToList();
            var completedFinancials = completedBookings.Where(b => financials.ContainsKey(b.Id)).Select(b => financials[b.Id]).ToList();

            var model = new ReportDashboardViewModel
            {
                StartDate = startDate,
                EndDate = endDate,
                TotalBookings = bookings.Count,
                ConfirmedBookings = bookings.Count(b => b.Status == BookingStatus.Confirmed),
                CompletedBookings = bookings.Count(b => b.Status == BookingStatus.Completed),
                CancelledBookings = bookings.Count(b =>
                    b.Status == BookingStatus.Cancelled ||
                    b.Status == BookingStatus.Declined),
                ExpiredBookings = bookings.Count(b => b.Status == BookingStatus.Expired),
                TotalRevenue = reportFinancials.Sum(f => f.TotalVerifiedPayments),
                TotalBookingValue = reportFinancials.Sum(f => f.BookingTotal),
                TotalCollectedRevenue = reportFinancials.Sum(f => f.TotalVerifiedPayments),
                TotalDownPaymentsCollected = reportFinancials.Sum(f => f.DownPaymentCollected),
                OutstandingReceivables = reportFinancials.Sum(f => f.RemainingBalance),
                CompletedBookingValue = completedFinancials.Sum(f => f.BookingTotal),
                CompletedCollectedRevenue = completedFinancials.Sum(f => f.TotalVerifiedPayments),
                CompletedOutstandingReceivables = completedFinancials.Sum(f => f.RemainingBalance),
                RefundsIssued = reportFinancials.Sum(f => f.CompletedRefunds),
                NetCollectedRevenue = reportFinancials.Sum(f => f.NetCollected),
                FullyPaidBookings = reportFinancials.Count(f => f.IsFullyPaid),
                PartiallyPaidBookings = reportFinancials.Count(f => f.TotalVerifiedPayments > 0 && !f.IsFullyPaid),
                UnpaidBookings = reportFinancials.Count(f => f.TotalVerifiedPayments <= 0),
                BookingFinancials = financials,
                PendingCount = payments.Count(p => p.Status == PaymentStatus.Pending),
                VerifiedCount = verifiedPayments.Count,
                RejectedCount = payments.Count(p => p.Status == PaymentStatus.Rejected),
                BookingRecords = bookings.Take(50).ToList()
            };

            var successfulBookingCount =
                model.ConfirmedBookings + model.CompletedBookings;

            model.ConfirmationRate = model.TotalBookings == 0
                ? 0
                : Math.Round(
                    (double)successfulBookingCount / model.TotalBookings * 100,
                    1);

            model.AverageBookingValue = successfulBookingCount == 0
                ? 0
                : Math.Round(model.TotalBookingValue / successfulBookingCount, 2);

            var paymentsWithProcessingTime = verifiedPayments
                .Where(p => p.VerifiedAt.HasValue)
                .ToList();

            model.AveragePaymentProcessingDays = paymentsWithProcessingTime.Count == 0
                ? 0
                : Math.Round(
                    paymentsWithProcessingTime.Average(p =>
                        (p.VerifiedAt!.Value - p.UploadedAt).TotalDays),
                    1);

            var uniqueClients = bookings
                .Select(b => b.ClientId)
                .Distinct()
                .Count();

            var repeatClients = bookings
                .GroupBy(b => b.ClientId)
                .Count(group => group.Count() > 1);

            model.CustomerRetentionRate = uniqueClients == 0
                ? 0
                : Math.Round((double)repeatClients / uniqueClients * 100, 1);

            BuildMonthlyReportData(model, bookings, verifiedPayments, endDate);
            BuildPackageTrendData(model, bookings);

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> ExportCsv(DateTime? startDate, DateTime? endDate)
        {
            var query = _context.Bookings
                .Include(b => b.Client)
                .Include(b => b.Service)
                .AsNoTracking()
                .AsQueryable();

            if (startDate.HasValue)
            {
                var start = startDate.Value.Date;
                query = query.Where(b => b.EventDate >= start);
            }

            if (endDate.HasValue)
            {
                var endExclusive = endDate.Value.Date.AddDays(1);
                query = query.Where(b => b.EventDate < endExclusive);
            }

            var bookings = await query
                .OrderByDescending(b => b.EventDate)
                .ThenByDescending(b => b.StartTime)
                .ToListAsync();

            var financials = await _financialService.GetSummariesAsync(bookings.Select(b => b.Id));
            var csv = new StringBuilder();
            csv.AppendLine(
                "Booking ID,Client,Package,Event Type,Event Date,Start Time,Venue,Booking Value,Collected,Outstanding,Payment Status,Completed Refunds,Net Collected,Booking Status");

            foreach (var booking in bookings)
            {
                financials.TryGetValue(booking.Id, out var finance);
                csv.AppendLine(string.Join(",",
                    EscapeCsv(booking.Id.ToString()),
                    EscapeCsv(booking.Client?.FullName ?? "Unknown"),
                    EscapeCsv(booking.PackageName),
                    EscapeCsv(booking.EventType),
                    EscapeCsv(booking.EventDate.ToString("yyyy-MM-dd")),
                    EscapeCsv(booking.StartTime.ToString("hh:mm tt")),
                    EscapeCsv(booking.PartyVenue),
                    EscapeCsv((finance?.BookingTotal ?? booking.FinalPrice).ToString("0.00")),
                    EscapeCsv((finance?.TotalVerifiedPayments ?? 0m).ToString("0.00")),
                    EscapeCsv((finance?.RemainingBalance ?? booking.FinalPrice).ToString("0.00")),
                    EscapeCsv(finance?.PaymentStatus ?? BookingPaymentState.Unpaid),
                    EscapeCsv((finance?.CompletedRefunds ?? 0m).ToString("0.00")),
                    EscapeCsv((finance?.NetCollected ?? 0m).ToString("0.00")),
                    EscapeCsv(booking.Status)));
            }

            var filename =
                $"AriesMagic-Booking-Report-{DateTime.Now:yyyyMMdd-HHmmss}.csv";

            return File(
                Encoding.UTF8.GetBytes(csv.ToString()),
                "text/csv",
                filename);
        }

        private static void BuildMonthlyReportData(
            ReportDashboardViewModel model,
            List<Booking> bookings,
            List<Payment> verifiedPayments,
            DateTime? selectedEndDate)
        {
            var referenceDate = selectedEndDate?.Date ?? DateTime.Today;
            var lastSixMonths = Enumerable.Range(0, 6)
                .Select(index =>
                    new DateTime(referenceDate.Year, referenceDate.Month, 1)
                        .AddMonths(-5 + index))
                .ToList();

            foreach (var monthStart in lastSixMonths)
            {
                var monthEnd = monthStart.AddMonths(1);

                model.MonthlyLabels.Add(monthStart.ToString("MMM yyyy"));
                model.MonthlyBookingCounts.Add(bookings.Count(b =>
                    b.EventDate >= monthStart && b.EventDate < monthEnd));

                model.MonthlyRevenue.Add(verifiedPayments
                    .Where(p =>
                        (p.VerifiedAt ?? p.UploadedAt) >= monthStart &&
                        (p.VerifiedAt ?? p.UploadedAt) < monthEnd)
                    .Sum(p => p.Amount));
            }
        }

        private static void BuildPackageTrendData(
            ReportDashboardViewModel model,
            List<Booking> bookings)
        {
            var packageTrends = bookings
                .Where(b => !string.IsNullOrWhiteSpace(b.PackageName))
                .GroupBy(b => b.PackageName)
                .Select(group => new
                {
                    PackageName = group.Key,
                    BookingCount = group.Count()
                })
                .OrderByDescending(item => item.BookingCount)
                .Take(8)
                .ToList();

            model.PackageLabels = packageTrends
                .Select(item => item.PackageName)
                .ToList();

            model.PackageBookingCounts = packageTrends
                .Select(item => item.BookingCount)
                .ToList();
        }

        private static string EscapeCsv(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            {
                return $"\"{value.Replace("\"", "\"\"")}\"";
            }

            return value;
        }
    }
}
