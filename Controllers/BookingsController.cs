using Microsoft.AspNetCore.Mvc;
using AriesMagicAppointmentSystem.Data;
using Microsoft.AspNetCore.Authorization;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using AriesMagicAppointmentSystem.Models;
namespace AriesMagicAppointmentSystem.Controllers

{
    public class BookingsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        [Authorize(Roles = "Staff,Admin")]
        public IActionResult Pending()
        {
            return RedirectToAction(nameof(Index), new { bookingStatus = "Pending" });
        }

        public BookingsController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }
        [Authorize(Roles = "Staff,Admin")]
        public async Task<IActionResult> Index(string? search, string? bookingStatus, string? paymentStatus)
        {
            var bookingsQuery = _context.Bookings
                .Include(b => b.Client)
                .Include(b => b.Service)
                .Include(b => b.Payments)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var lowered = search.ToLower();

                bookingsQuery = bookingsQuery.Where(b =>
                    ($"BK-{b.CreatedAt.Year}-{b.Id:D3}").ToLower().Contains(lowered) ||
                    (b.Client != null && b.Client.FullName.ToLower().Contains(lowered)) ||
                    (b.Client != null && b.Client.Email.ToLower().Contains(lowered)) ||
                    (b.Service != null && b.Service.Name.ToLower().Contains(lowered)));
            }

            if (!string.IsNullOrWhiteSpace(bookingStatus) && bookingStatus != "All")
            {
                bookingsQuery = bookingsQuery.Where(b => b.Status == bookingStatus);
            }

            var bookings = await bookingsQuery
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();

            var rows = bookings.Select(b =>
            {
                var latestPayment = b.Payments
                    .OrderByDescending(p => p.UploadedAt)
                    .FirstOrDefault();

                var latestPaymentStatus = latestPayment?.Status ?? "No Payment";

                return new BookingManagementRowViewModel
                {
                    Id = b.Id,
                    BookingCode = $"BK-{b.CreatedAt.Year}-{b.Id:D3}",
                    ClientName = b.Client?.FullName ?? "N/A",
                    ServiceName = b.Service?.Name ?? "N/A",
                    EventDate = b.EventDate,
                    BookingStatus = b.Status,
                    PaymentStatus = latestPaymentStatus,
                    InternalNotes = b.InternalNotes
                };
            }).ToList();

            if (!string.IsNullOrWhiteSpace(paymentStatus) && paymentStatus != "All")
            {
                rows = rows.Where(r => r.PaymentStatus == paymentStatus).ToList();
            }

            var viewModel = new BookingManagementViewModel
            {
                Search = search,
                BookingStatus = bookingStatus ?? "All",
                PaymentStatus = paymentStatus ?? "All",
                Bookings = rows
            };

            return View(viewModel);
        }

    }
}