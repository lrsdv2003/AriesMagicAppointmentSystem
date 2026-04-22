using AriesMagicAppointmentSystem.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AriesMagicAppointmentSystem.Models;
namespace AriesMagicAppointmentSystem.Controllers
{
    [Authorize(Roles = "Staff,Admin")]
    public class CalendarController : Controller
    {
        private readonly ApplicationDbContext _context;

        public CalendarController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var bookings = await _context.Bookings
                .Include(b => b.Client)
                .Include(b => b.Service)
                .Where(b => b.Status == BookingStatus.Confirmed)
                .ToListAsync();

            return View(bookings);
        }

        [Authorize(Roles = "Staff,Admin")]
        [HttpGet]
        public async Task<IActionResult> GetReservationsByDate(DateTime date)
        {
            var reservations = await _context.Bookings
                .Include(b => b.Client)
                .Include(b => b.Service)
                .Where(b => b.EventDate.Date == date.Date)
                .OrderBy(b => b.StartTime)
                .Select(b => new
                {
                    bookingId = b.Id,
                    bookingCode = $"BK-{b.CreatedAt.Year}-{b.Id:D3}",
                    serviceName = b.Service != null ? b.Service.Name : "N/A",
                    clientName = b.Client != null ? b.Client.FullName : "N/A",
                    eventDate = b.EventDate,
                    startTime = b.StartTime,
                    endTime = b.EndTime,
                    durationHours = b.Service != null ? b.Service.DurationInHours : 0,
                    status = b.Status.ToString(),
                    totalAmount = b.Payments,
                    detailsUrl = Url.Action("Details", "Bookings", new { id = b.Id })
                })
                .ToListAsync();

            return Json(reservations);
        }
    }
}