using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AriesMagicAppointmentSystem.Controllers
{
    [Authorize(Roles = "Admin")]
    public class ReportsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ReportsController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var bookings = await _context.Bookings
                .Include(b => b.Client)
                .Include(b => b.Service)
                .ToListAsync();

            var payments = await _context.Payments
                .Include(p => p.Booking)
                .ToListAsync();

            var now = DateTime.Now;
            var sixMonths = Enumerable.Range(0, 6)
                .Select(i => new DateTime(now.Year, now.Month, 1).AddMonths(-5 + i))
                .ToList();

            var model = new ReportDashboardViewModel
            {
                TotalBookings = bookings.Count,
                ConfirmedBookings = bookings.Count(b => b.Status == BookingStatus.Confirmed),
                ExpiredBookings = bookings.Count(b => b.Status == BookingStatus.Expired),
                CancelledBookings = bookings.Count(b => b.Status == BookingStatus.Declined),

                TotalRevenue = payments
                    .Where(p => p.Status == PaymentStatus.Verified)
                    .Sum(p => p.Amount),

                PendingCount = payments.Count(p => p.Status == PaymentStatus.Pending),
                VerifiedCount = payments.Count(p => p.Status == PaymentStatus.Verified),
                RejectedCount = payments.Count(p => p.Status == PaymentStatus.Rejected)
            };

            model.ConfirmationRate = model.TotalBookings == 0
                ? 0
                : Math.Round((double)model.ConfirmedBookings / model.TotalBookings * 100, 1);

            model.AverageBookingValue = model.ConfirmedBookings == 0
                ? 0
                : Math.Round(model.TotalRevenue / Math.Max(model.ConfirmedBookings, 1), 2);

            var verifiedPayments = payments
                .Where(p => p.VerifiedAt.HasValue)
                .ToList();

            model.AveragePaymentProcessingDays = verifiedPayments.Count == 0
                ? 0
                : Math.Round(
                    verifiedPayments.Average(p => (p.VerifiedAt!.Value - p.UploadedAt).TotalDays),
                    1
                );

            var repeatClients = bookings
                .GroupBy(b => b.ClientId)
                .Count(g => g.Count() > 1);

            var uniqueClients = bookings
                .Select(b => b.ClientId)
                .Distinct()
                .Count();

            model.CustomerRetentionRate = uniqueClients == 0
                ? 0
                : Math.Round((double)repeatClients / uniqueClients * 100, 1);

            foreach (var monthStart in sixMonths)
            {
                var monthEnd = monthStart.AddMonths(1);

                model.MonthlyLabels.Add(monthStart.ToString("MMM"));

                model.MonthlyBookingCounts.Add(bookings.Count(b =>
                    b.EventDate >= monthStart && b.EventDate < monthEnd));

                model.MonthlyRevenue.Add(payments
                    .Where(p => p.UploadedAt >= monthStart && p.UploadedAt < monthEnd)
                    .Where(p => p.Status == PaymentStatus.Verified)
                    .Sum(p => p.Amount));
            }

            return View(model);
        }
    }
}