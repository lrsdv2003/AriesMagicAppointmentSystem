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
    [Authorize(Roles = "Owner,Admin,Staff")]
    public class DashboardController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IPaymentFinancialService _financialService;

        public DashboardController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            IPaymentFinancialService financialService)
        {
            _context = context;
            _userManager = userManager;
            _financialService = financialService;
        }

        public IActionResult Index()
        {
            if (User.IsInRole("Owner"))
            {
                return RedirectToAction(nameof(Owner));
            }

            if (User.IsInRole("Admin"))
            {
                return RedirectToAction(nameof(Admin));
            }

            return RedirectToAction(nameof(Staff));
        }

        [Authorize(Roles = "Owner")]
        public async Task<IActionResult> Owner()
        {
            var today = DateTime.Today;
            var monthStart = new DateTime(today.Year, today.Month, 1);

            var model = new RoleDashboardViewModel
            {
                RoleName = "Owner",
                PendingPayments = await _context.Payments
                    .AsNoTracking()
                    .CountAsync(p => p.Status == PaymentStatus.Pending),
                PendingRefunds = await _context.RefundRequests
                    .AsNoTracking()
                    .CountAsync(r => r.Status == RefundStatus.Pending || r.Status == RefundStatus.UnderReview),
                RefundProofsAwaitingVerification = await _context.RefundRequests
                    .AsNoTracking()
                    .CountAsync(r => r.Status == RefundStatus.RefundProcessing),
                PaymentsAdditionalEvidence = await _context.Payments.AsNoTracking().CountAsync(p => p.Status == PaymentStatus.AdditionalEvidenceRequired),
                UpcomingBookings = await _context.Bookings
                    .AsNoTracking()
                    .CountAsync(b => b.EventDate >= today && b.Status == BookingStatus.Confirmed),
                CurrentMonthRevenue = await _context.Payments
                    .AsNoTracking()
                    .Where(p => p.Status == PaymentStatus.Verified && p.VerifiedAt >= monthStart)
                    .SumAsync(p => (decimal?)p.Amount) ?? 0,
                RecentPaymentsToVerify = await _context.Payments
                    .AsNoTracking()
                    .Include(p => p.Booking)
                        .ThenInclude(b => b!.Client)
                    .Include(p => p.Booking)
                        .ThenInclude(b => b!.Service)
                    .Where(p => p.Status == PaymentStatus.Pending)
                    .OrderByDescending(p => p.UploadedAt)
                    .Take(5)
                    .ToListAsync(),
                UpcomingEvents = await _context.Bookings
                    .AsNoTracking()
                    .Include(b => b.Client)
                    .Include(b => b.Service)
                    .Where(b => b.EventDate >= today && b.Status == BookingStatus.Confirmed)
                    .OrderBy(b => b.EventDate)
                    .ThenBy(b => b.StartTime)
                    .Take(5)
                    .ToListAsync()
            };

            var financialBookingIds = await _context.Bookings.AsNoTracking()
                .Where(b => b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.Declined && b.Status != BookingStatus.Expired)
                .Select(b => b.Id).ToListAsync();
            var financials = await _financialService.GetSummariesAsync(financialBookingIds);
            model.VerifiedRevenue = financials.Values.Sum(f => f.TotalVerifiedPayments);
            model.OutstandingReceivables = financials.Values.Sum(f => f.RemainingBalance);
            model.NetCollectedRevenue = financials.Values.Sum(f => f.NetCollected);
            var completedRefundsThisMonth = await _context.RefundRequests.AsNoTracking()
                .Where(r => r.Status == RefundStatus.Refunded && r.RefundCompletedAt >= monthStart)
                .SumAsync(r => (decimal?)(r.ApprovedAmount ?? r.Amount)) ?? 0m;
            model.CurrentMonthRevenue = Math.Max(0m, model.CurrentMonthRevenue - completedRefundsThisMonth);

            return View(model);
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Admin()
        {
            var model = await BuildCommonDashboardAsync("Admin");

            var clients = await _userManager.GetUsersInRoleAsync("Client");
            var staff = await _userManager.GetUsersInRoleAsync("Staff");
            var allUsers = await _userManager.Users.ToListAsync();

            model.ActiveClients = clients.Count(u => u.IsActive);
            model.ActiveStaff = staff.Count(u => u.IsActive);
            model.TotalUsers = allUsers.Count;

            model.ActivePackages = await _context.Services
                .CountAsync(s => !s.IsArchived);

            model.ArchivedPackages = await _context.Services
                .CountAsync(s => s.IsArchived);

            model.BlockedDates = await _context.BlockedDates
                .CountAsync(b => b.Date >= DateTime.Today);

            model.PendingReschedules = await _context.RescheduleRequests
                .CountAsync(r =>
                    r.Status == RescheduleRequestStatus.Pending);

            // Count trashed/failed booking requests
            model.TrashedBookingsCount = await _context.Bookings
                .CountAsync(b => b.Status == BookingStatus.Declined ||
                               b.Status == BookingStatus.Cancelled ||
                               b.Status == BookingStatus.Expired);

            // Clear booking-related stats that are not relevant for Admin
            model.TotalBookings = 0;
            model.PendingBookings = 0;
            model.ConfirmedBookings = 0;
            model.CompletedBookings = 0;
            model.UpcomingBookings = 0;
            model.UpcomingEvents = new List<Booking>();
            model.RecentBookings = new List<Booking>();

            return View(model);
        }

        [Authorize(Roles = "Staff")]
        public async Task<IActionResult> Staff()
        {
            var today = DateTime.Today;

            var model = new RoleDashboardViewModel
            {
                RoleName = "Staff",
                PendingBookings = await _context.Bookings
                    .AsNoTracking()
                    .CountAsync(b => b.Status == BookingStatus.Pending),
                UpcomingBookings = await _context.Bookings
                    .AsNoTracking()
                    .CountAsync(b => b.EventDate >= today && b.Status == BookingStatus.Confirmed),
                ActivePackages = await _context.Services
                    .AsNoTracking()
                    .CountAsync(s => !s.IsArchived),
                PendingReschedules = await _context.RescheduleRequests
                    .AsNoTracking()
                    .CountAsync(r => r.Status == RescheduleRequestStatus.Pending),
                RecentBookingRequests = await _context.Bookings
                    .AsNoTracking()
                    .Include(b => b.Client)
                    .Include(b => b.Service)
                    .OrderByDescending(b => b.CreatedAt)
                    .Take(5)
                    .ToListAsync(),
                RecentRescheduleRequests = await _context.RescheduleRequests
                    .AsNoTracking()
                    .Include(r => r.Booking)
                        .ThenInclude(b => b!.Client)
                    .Include(r => r.Booking)
                        .ThenInclude(b => b!.Service)
                    .OrderByDescending(r => r.CreatedAt)
                    .Take(5)
                    .ToListAsync()
            };

            return View(model);
        }

        private async Task<RoleDashboardViewModel>
            BuildCommonDashboardAsync(string roleName)
        {
            var today = DateTime.Today;

            var model = new RoleDashboardViewModel
            {
                RoleName = roleName,

                TotalBookings = await _context.Bookings.CountAsync(),

                PendingBookings = await _context.Bookings
                    .CountAsync(b =>
                        b.Status == BookingStatus.Pending),

                ConfirmedBookings = await _context.Bookings
                    .CountAsync(b =>
                        b.Status == BookingStatus.Confirmed),

                CompletedBookings = await _context.Bookings
                    .CountAsync(b =>
                        b.Status == BookingStatus.Completed),

                PendingPayments = await _context.Payments.CountAsync(p => p.Status == PaymentStatus.Pending),
                PaymentsAdditionalEvidence = await _context.Payments.CountAsync(p => p.Status == PaymentStatus.AdditionalEvidenceRequired),
                PendingRefunds = await _context.RefundRequests.CountAsync(r => r.Status == RefundStatus.Pending || r.Status == RefundStatus.UnderReview),
                RefundProofsAwaitingVerification = await _context.RefundRequests.CountAsync(r => r.Status == RefundStatus.RefundProcessing),

                UpcomingBookings = await _context.Bookings
                    .CountAsync(b =>
                        b.EventDate >= today &&
                        b.Status == BookingStatus.Confirmed),

                UpcomingEvents = await _context.Bookings
                    .Include(b => b.Service)
                    .Include(b => b.Client)
                    .Where(b =>
                        b.EventDate >= today &&
                        b.Status == BookingStatus.Confirmed)
                    .OrderBy(b => b.EventDate)
                    .ThenBy(b => b.StartTime)
                    .Take(6)
                    .ToListAsync(),

                RecentBookings = await _context.Bookings
                    .Include(b => b.Service)
                    .Include(b => b.Client)
                    .OrderByDescending(b => b.CreatedAt)
                    .Take(6)
                    .ToListAsync()
            };

            return model;
        }
    }
}