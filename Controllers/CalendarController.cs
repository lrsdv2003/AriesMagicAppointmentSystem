using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AriesMagicAppointmentSystem.Controllers
{
    [Authorize(Roles = "Admin,Staff")]
    public class CalendarController : Controller
    {
        private readonly ApplicationDbContext _context;

        public CalendarController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var confirmedBookings = await _context.Bookings
                .Include(b => b.Client)
                .Include(b => b.Service)
                .Where(b => b.Status == BookingStatus.Confirmed)
                .ToListAsync();

            return View(confirmedBookings);
        }
    }
}