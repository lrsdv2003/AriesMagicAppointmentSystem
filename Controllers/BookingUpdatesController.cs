using System.Security.Claims;
using AriesMagicAppointmentSystem.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AriesMagicAppointmentSystem.Controllers;

[Authorize(Roles = "Client,Staff,Owner")]
[Route("booking-updates")]
public class BookingUpdatesController : Controller
{
    private readonly ApplicationDbContext _db;
    public BookingUpdatesController(ApplicationDbContext db) => _db = db;

    [HttpGet("snapshot")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Snapshot(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();
        var mine = User.IsInRole("Client");
        var bookings = _db.Bookings.AsNoTracking();
        if (mine) bookings = bookings.Where(b => b.ApplicationUserId == userId);
        var recent = await bookings.OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.Id)
            .Select(b => new { b.Id, b.Status, b.CreatedAt })
            .Take(100).ToListAsync(cancellationToken);
        var pending = await bookings.CountAsync(b => b.Status == "Pending", cancellationToken);
        var notifications = await _db.Notifications.AsNoTracking().CountAsync(
            n => n.UserId == userId && !n.IsRead, cancellationToken);
        return Json(new { bookings = recent, pending, unreadNotifications = notifications });
    }
}
