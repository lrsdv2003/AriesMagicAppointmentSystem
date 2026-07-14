using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
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

        public DashboardController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
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
            var model = await BuildCommonDashboardAsync("Owner");

            var monthStart = new DateTime(
                DateTime.Today.Year,
                DateTime.Today.Month,
                1);

            model.PendingPayments = await _context.Payments
                .CountAsync(p => p.Status == PaymentStatus.Pending);

            model.PendingRefunds = await _context.RefundRequests
                .CountAsync(r => r.Status == RefundStatus.Pending);

            model.PendingReschedules = await _context.RescheduleRequests
                .CountAsync(r =>
                    r.Status == RescheduleRequestStatus.Pending);

            model.VerifiedRevenue = await _context.Payments
                .Where(p => p.Status == PaymentStatus.Verified)
                .SumAsync(p => (decimal?)p.Amount) ?? 0;

            model.CurrentMonthRevenue = await _context.Payments
                .Where(p =>
                    p.Status == PaymentStatus.Verified &&
                    p.VerifiedAt >= monthStart)
                .SumAsync(p => (decimal?)p.Amount) ?? 0;

            return View(model);
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Admin()
        {
            var model = await BuildCommonDashboardAsync("Admin");

            var clients = await _userManager.GetUsersInRoleAsync("Client");
            var staff = await _userManager.GetUsersInRoleAsync("Staff");

            model.ActiveClients = clients.Count(u => u.IsActive);
            model.ActiveStaff = staff.Count(u => u.IsActive);

            model.ActivePackages = await _context.Services
                .CountAsync(s => !s.IsArchived);

            model.ArchivedPackages = await _context.Services
                .CountAsync(s => s.IsArchived);

            model.BlockedDates = await _context.BlockedDates
                .CountAsync(b => b.Date >= DateTime.Today);

            model.PendingReschedules = await _context.RescheduleRequests
                .CountAsync(r =>
                    r.Status == RescheduleRequestStatus.Pending);

            return View(model);
        }

        [Authorize(Roles = "Staff")]
        public async Task<IActionResult> Staff()
        {
            var model = await BuildCommonDashboardAsync("Staff");

            model.ActivePackages = await _context.Services
                .CountAsync(s => !s.IsArchived);

            model.PendingReschedules = await _context.RescheduleRequests
                .CountAsync(r =>
                    r.Status == RescheduleRequestStatus.Pending);

            model.RecentRescheduleRequests =
                await _context.RescheduleRequests
                    .Include(r => r.Booking)
                    .ThenInclude(b => b!.Service)
                    .OrderByDescending(r => r.CreatedAt)
                    .Take(5)
                    .ToListAsync();

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