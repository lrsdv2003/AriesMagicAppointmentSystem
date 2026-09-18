using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

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
            var appUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrWhiteSpace(appUserId))
            {
                return View(new List<AriesMagicAppointmentSystem.Models.Notification>());
            }

            var notifications = await _context.Notifications
                .Where(n => n.UserId == appUserId)
                .OrderByDescending(n => n.CreatedAt)
                .ToListAsync();

            return View(notifications);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAllRead()
        {
            var appUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(appUserId)) return Unauthorized();
            await _context.Notifications.Where(n => n.UserId == appUserId && !n.IsRead)
                .ExecuteUpdateAsync(set => set.SetProperty(n => n.IsRead, true)
                    .SetProperty(n => n.ReadAt, DateTime.Now));
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Open(int id)
        {
            var appUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrWhiteSpace(appUserId))
                return RedirectToAction("Index", "Home");

            var notification = await _context.Notifications
                .FirstOrDefaultAsync(n => n.Id == id && n.UserId == appUserId);

            if (notification == null) return NotFound();

            if (!notification.IsRead)
            {
                notification.IsRead = true;
                notification.ReadAt = DateTime.Now;
                await _context.SaveChangesAsync();
            }

            if (!string.IsNullOrWhiteSpace(notification.Link) && Url.IsLocalUrl(notification.Link) && User.CanFollowModuleLink(notification.Link))
            {
                return Redirect(notification.Link);
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
