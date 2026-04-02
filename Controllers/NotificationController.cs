using AriesMagicAppointmentSystem.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AriesMagicAppointmentSystem.Controllers
{
    [Authorize]
    public class NotificationsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public NotificationsController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            int? legacyUserId = await GetLegacyUserIdAsync();
            if (legacyUserId == null)
            {
                return View(new List<AriesMagicAppointmentSystem.Models.Notification>());
            }

            var notifications = await _context.Notifications
                .Where(n => n.UserId == legacyUserId)
                .OrderByDescending(n => n.CreatedAt)
                .ToListAsync();

            return View(notifications);
        }

        public async Task<IActionResult> Open(int id)
        {
            int? legacyUserId = await GetLegacyUserIdAsync();
            if (legacyUserId == null) return RedirectToAction("Index", "Home");

            var notification = await _context.Notifications
                .FirstOrDefaultAsync(n => n.Id == id && n.UserId == legacyUserId);

            if (notification == null) return NotFound();

            notification.IsRead = true;
            notification.ReadAt = DateTime.Now;
            await _context.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(notification.Link))
            {
                return Redirect(notification.Link);
            }

            return RedirectToAction(nameof(Index));
        }

        private async Task<int?> GetLegacyUserIdAsync()
        {
            var email = User.Identity?.Name;

            if (string.IsNullOrWhiteSpace(email))
                return null;

            var legacyUser = await _context.LegacyUsers
                .FirstOrDefaultAsync(u => u.Email == email);

            return legacyUser?.Id;
        }
    }
}