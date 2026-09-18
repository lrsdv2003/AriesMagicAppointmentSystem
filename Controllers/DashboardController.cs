using System.Security.Claims;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using AriesMagicAppointmentSystem.Services;
using AriesMagicAppointmentSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AriesMagicAppointmentSystem.Controllers;

[Authorize(Roles = "Owner,Admin,Staff")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class DashboardController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IPaymentFinancialService _financialService;
    private readonly IHistoryService _historyService;
    private readonly ILogger<DashboardController> _logger;

    public DashboardController(ApplicationDbContext context, IPaymentFinancialService financialService,
        IHistoryService historyService, ILogger<DashboardController> logger)
    {
        _context = context;
        _financialService = financialService;
        _historyService = historyService;
        _logger = logger;
    }

    public IActionResult Index() => RedirectToAction(User.IsInRole("Owner") ? nameof(Owner)
        : User.IsInRole("Admin") ? nameof(Admin) : nameof(Staff));

    private async Task LoadAsync(RoleDashboardViewModel model, string section, Func<Task> load)
    {
        try { await load(); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to load {Role} dashboard section {Section}", model.RoleName, section);
            model.UnavailableSections.Add(section);
        }
    }

    private async Task<RoleDashboardViewModel> StartAsync(string role)
    {
        var model = new RoleDashboardViewModel { RoleName = role, DisplayName = role };
        var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
        await LoadAsync(model, "profile", async () =>
        {
            var name = await _context.Users.AsNoTracking().Where(u => u.Id == uid).Select(u => u.FullName).FirstOrDefaultAsync();
            if (!string.IsNullOrWhiteSpace(name)) model.DisplayName = name;
        });
        await LoadAsync(model, "messages", async () =>
        {
            model.UnreadConversations = await _context.ConversationParticipants.AsNoTracking()
                .CountAsync(p => p.UserId == uid && p.Conversation!.Messages.Any(m =>
                    m.SenderId != uid && (!p.LastReadAt.HasValue || m.SentAt > p.LastReadAt.Value)));
        });
        return model;
    }

    private void AddAttention(RoleDashboardViewModel model, string label, int count, string href, string icon)
    {
        if (count > 0) model.Attention.Add(new(label, href, icon, count.ToString("N0")));
    }

    private void AddMessages(RoleDashboardViewModel model)
    {
        if (!model.UnavailableSections.Contains("messages"))
            AddAttention(model, "Unread conversations", model.UnreadConversations, "/Communications?filter=unread", "bi-chat-dots");
    }

    private async Task LoadScheduleAsync(RoleDashboardViewModel model)
    {
        await LoadAsync(model, "schedule", async () =>
        {
            var today = model.AsOf.Date;
            var tomorrow = today.AddDays(1);
            var events = _context.Bookings.AsNoTracking().Include(b => b.Client).Include(b => b.Service)
                .Where(b => b.Status == BookingStatus.Confirmed && b.EndTime >= model.AsOf);
            model.TodayBookings = await events.CountAsync(b => b.EventDate >= today && b.EventDate < tomorrow);
            model.UpcomingBookings = await events.CountAsync(b => b.EventDate >= tomorrow);
            model.TodayEvents = await events.Where(b => b.EventDate >= today && b.EventDate < tomorrow)
                .OrderBy(b => b.StartTime).ThenBy(b => b.Id).Take(3).ToListAsync();
            model.UpcomingEvents = await events.Where(b => b.EventDate >= tomorrow)
                .OrderBy(b => b.EventDate).ThenBy(b => b.StartTime).Take(4).ToListAsync();
        });
    }

    private async Task LoadActivityAsync(RoleDashboardViewModel model, SystemActivityType[] types)
    {
        await LoadAsync(model, "activity", async () =>
        {
            model.RecentActivity = await _context.SystemActivities.AsNoTracking().Where(a => types.Contains(a.Type))
                .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).Take(5).ToListAsync();
        });
    }

    [Authorize(Roles = "Staff")]
    public async Task<IActionResult> Staff()
    {
        var model = await StartAsync("Staff");
        await LoadAsync(model, "schedule refresh", async () => { await _historyService.ArchiveDueBookingsAsync(); });
        await LoadAsync(model, "requests", async () =>
        {
            var pending = _context.Bookings.AsNoTracking().Where(b => b.Status == BookingStatus.Pending);
            model.PendingBookings = await pending.CountAsync();
            model.PendingReschedules = await _context.RescheduleRequests.CountAsync(r => r.Status == RescheduleRequestStatus.Pending);
            model.RecentBookingRequests = await pending.Include(b => b.Client).Include(b => b.Service)
                .OrderBy(b => b.CreatedAt).ThenBy(b => b.Id).Take(4).ToListAsync();
            model.RecentRescheduleRequests = await _context.RescheduleRequests.AsNoTracking().Include(r => r.Booking).ThenInclude(b => b!.Client)
                .Where(r => r.Status == RescheduleRequestStatus.Pending).OrderBy(r => r.CreatedAt).Take(3).ToListAsync();
        });
        await LoadScheduleAsync(model);
        await LoadActivityAsync(model, [SystemActivityType.BookingApproved, SystemActivityType.BookingDeclined,
            SystemActivityType.RescheduleApproved, SystemActivityType.RescheduleRejected]);
        if (!model.UnavailableSections.Contains("requests"))
        {
            AddAttention(model, "Booking requests", model.PendingBookings, "/Bookings?bookingStatus=Pending", "bi-journal-text");
            AddAttention(model, "Reschedule requests", model.PendingReschedules, "/RescheduleRequests?status=Pending", "bi-arrow-left-right");
            model.Metrics.Add(new("Pending bookings", model.PendingBookings.ToString("N0"), "Awaiting Staff review"));
            model.Metrics.Add(new("Pending reschedules", model.PendingReschedules.ToString("N0"), "Awaiting Staff review"));
        }
        if (!model.UnavailableSections.Contains("schedule"))
        {
            model.Metrics.Add(new("Today's events", model.TodayBookings.ToString("N0"), "Confirmed; in progress or upcoming"));
            model.Metrics.Add(new("Upcoming events", model.UpcomingBookings.ToString("N0"), "Confirmed; from tomorrow"));
        }
        AddMessages(model);
        model.QuickActions = [
            new("Booking Requests", "/Bookings?bookingStatus=Pending", "bi-journal-text"),
            new("Reschedules", "/RescheduleRequests?status=Pending", "bi-arrow-left-right"),
            new("Calendar", "/Calendar", "bi-calendar3"),
            new("Packages", "/Services", "bi-box-seam"),
            new("Messages", "/Communications", "bi-chat-dots")];
        return View(model);
    }

    [Authorize(Roles = "Owner")]
    public async Task<IActionResult> Owner()
    {
        var model = await StartAsync("Owner");
        await LoadAsync(model, "schedule refresh", async () => { await _historyService.ArchiveDueBookingsAsync(); });
        await LoadAsync(model, "queues", async () =>
        {
            model.PendingPayments = await _context.Payments.CountAsync(p => p.Status == PaymentStatus.Pending);
            model.PendingRefunds = await _context.RefundRequests.CountAsync(r => r.Status == RefundStatus.Pending || r.Status == RefundStatus.UnderReview);
            model.RefundProofsAwaitingVerification = await _context.RefundRequests.CountAsync(r => r.Status == RefundStatus.RefundProcessing);
            model.RecentPaymentsToVerify = await _context.Payments.AsNoTracking().Include(p => p.Booking).ThenInclude(b => b!.Client)
                .Include(p => p.OcrVerifications).Where(p => p.Status == PaymentStatus.Pending)
                .OrderBy(p => p.UploadedAt).ThenBy(p => p.Id).Take(4).ToListAsync();
            model.RecentRefundRequests = await _context.RefundRequests.AsNoTracking().Include(r => r.Booking).ThenInclude(b => b!.Client)
                .Where(r => r.Status == RefundStatus.Pending || r.Status == RefundStatus.UnderReview || r.Status == RefundStatus.RefundProcessing)
                .OrderBy(r => r.RequestedAt).ThenBy(r => r.Id).Take(4).ToListAsync();
        });
        await LoadAsync(model, "financial overview", async () =>
        {
            // Same all-time cohort and financial service as unfiltered Reports.
            var bookings = await _context.Bookings.AsNoTracking().Select(b => new { b.Id, b.Status }).ToListAsync();
            var summaries = await _financialService.GetSummariesAsync(bookings.Select(b => b.Id));
            var activeIds = bookings.Where(b => b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.Declined
                && b.Status != BookingStatus.Expired).Select(b => b.Id).ToHashSet();
            model.VerifiedRevenue = summaries.Values.Sum(f => f.TotalVerifiedPayments);
            model.CompletedRefunds = summaries.Values.Sum(f => f.CompletedRefunds);
            model.NetCollectedRevenue = model.VerifiedRevenue - model.CompletedRefunds;
            model.BookingValue = summaries.Values.Where(f => activeIds.Contains(f.BookingId)).Sum(f => f.BookingTotal);
            model.OutstandingReceivables = summaries.Values.Where(f => activeIds.Contains(f.BookingId)).Sum(f => f.RemainingBalance);
        });
        await LoadAsync(model, "business overview", async () =>
        {
            var month = new DateTime(model.AsOf.Year, model.AsOf.Month, 1);
            var end = month.AddMonths(1);
            model.ConfirmedBookings = await _context.Bookings.CountAsync(b => b.Status == BookingStatus.Confirmed && b.EventDate >= month && b.EventDate < end);
            model.CompletedBookings = await _context.Bookings.CountAsync(b => b.Status == BookingStatus.Completed && b.EventDate >= month && b.EventDate < end);
            model.MostBookedPackage = await _context.Bookings.AsNoTracking()
                .Where(b => (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.Completed) && b.EventDate >= month && b.EventDate < end)
                .GroupBy(b => b.PackageName).OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
                .Select(g => g.Key).FirstOrDefaultAsync() ?? "No bookings this month";
            var first = month.AddMonths(-5);
            var volume = await _context.Bookings.AsNoTracking().Where(b => b.EventDate >= first && b.EventDate < end)
                .GroupBy(b => new { b.EventDate.Year, b.EventDate.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() }).ToListAsync();
            model.BookingTrend = Enumerable.Range(0, 6).Select(i => first.AddMonths(i))
                .Select(d => new DashboardTrendPoint(d.ToString("MMM yyyy"), volume.FirstOrDefault(v => v.Year == d.Year && v.Month == d.Month)?.Count ?? 0)).ToList();
        });
        await LoadScheduleAsync(model);
        await LoadActivityAsync(model, [SystemActivityType.PaymentVerified, SystemActivityType.PaymentRejected,
            SystemActivityType.RefundProcessed, SystemActivityType.RefundApproved, SystemActivityType.RefundRejected,
            SystemActivityType.ServiceUpdated, SystemActivityType.CalendarModified, SystemActivityType.RescheduleApproved]);
        if (!model.UnavailableSections.Contains("queues"))
        {
            AddAttention(model, "Verify payments", model.PendingPayments, "/Payments/PendingVerification?filter=awaiting", "bi-credit-card");
            AddAttention(model, "Review refunds", model.PendingRefunds, "/Payments/RefundRequests?filter=Pending", "bi-arrow-counterclockwise");
            AddAttention(model, "Verify refund proofs", model.RefundProofsAwaitingVerification, "/Payments/RefundRequests?filter=RefundProcessing", "bi-receipt");
        }
        if (!model.UnavailableSections.Contains("financial overview"))
        {
            string Money(decimal amount) => "PHP " + amount.ToString("N2");
            model.Metrics = [
                new("Collected revenue", Money(model.VerifiedRevenue), "All-time verified payments"),
                new("Outstanding balance", Money(model.OutstandingReceivables), "Relevant bookings; money still owed"),
                new("Booking value", Money(model.BookingValue), "Excludes cancelled, declined and expired"),
                new("Completed refunds", Money(model.CompletedRefunds), "Refunds actually paid"),
                new("Net collected", Money(model.NetCollectedRevenue), "Collected revenue minus completed refunds")];
        }
        AddMessages(model);
        model.QuickActions = [
            new("Verify Payments", "/Payments/PendingVerification", "bi-credit-card"),
            new("Refund Requests", "/Payments/RefundRequests", "bi-arrow-counterclockwise"),
            new("Reports", "/Reports", "bi-bar-chart"),
            new("Calendar", "/Calendar", "bi-calendar3"),
            new("Packages", "/Services", "bi-box-seam"),
            new("Messages", "/Communications", "bi-chat-dots")];
        return View(model);
    }

    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Admin()
    {
        var model = await StartAsync("Admin");
        await LoadAsync(model, "users", async () =>
        {
            var users = _context.Users.AsNoTracking();
            var now = DateTimeOffset.UtcNow;
            model.TotalUsers = await users.CountAsync();
            model.ActiveUsers = await users.CountAsync(u => u.IsActive && !(u.LockoutEnd > now));
            model.InactiveUsers = await users.CountAsync(u => !u.IsActive);
            model.UnverifiedUsers = await users.CountAsync(u => !u.EmailConfirmed);
            model.LockedUsers = await users.CountAsync(u => u.LockoutEnd > now);
            model.UserRoles = await (from ur in _context.UserRoles join r in _context.Roles on ur.RoleId equals r.Id
                group ur by r.Name into g select new { Role = g.Key ?? "Unassigned", Count = g.Count() })
                .ToDictionaryAsync(r => r.Role, r => r.Count);
        });
        await LoadAsync(model, "system overview", async () =>
        {
            model.ActivePackages = await _context.Services.CountAsync(s => !s.IsArchived);
            model.ArchivedPackages = await _context.Services.CountAsync(s => s.IsArchived);
            model.BlockedDates = await _context.BlockedDates.CountAsync(d => d.Date >= model.AsOf.Date);
            model.TrashedBookingsCount = await _context.Bookings.CountAsync(b =>
                b.Status == BookingStatus.Declined || b.Status == BookingStatus.Cancelled || b.Status == BookingStatus.Expired);
            var since = DateTime.UtcNow.AddHours(-24);
            model.FailedLogins = await _context.SystemActivities.CountAsync(a => a.Type == SystemActivityType.LoginFailed && a.CreatedAt >= DateTime.UtcNow.Date);
            model.RecentActivityCount = await _context.SystemActivities.CountAsync(a => a.CreatedAt >= since);
        });
        await LoadActivityAsync(model, [SystemActivityType.UserCreated, SystemActivityType.UserUpdated,
            SystemActivityType.UserEnabled, SystemActivityType.UserDisabled, SystemActivityType.RoleAssigned,
            SystemActivityType.RoleRemoved, SystemActivityType.ServiceCreated, SystemActivityType.ServiceUpdated,
            SystemActivityType.ServiceArchived, SystemActivityType.ServiceRestored, SystemActivityType.CalendarModified,
            SystemActivityType.SettingsChanged, SystemActivityType.LoginFailed]);
        if (!model.UnavailableSections.Contains("users"))
        {
            AddAttention(model, "Unverified accounts", model.UnverifiedUsers, "/UserManagement?verification=Unverified", "bi-person-exclamation");
            AddAttention(model, "Inactive accounts", model.InactiveUsers, "/UserManagement?status=Disabled", "bi-person-dash");
            AddAttention(model, "Locked accounts", model.LockedUsers, "/UserManagement?status=Locked", "bi-lock");
            model.Metrics.AddRange([
                new("Total users", model.TotalUsers.ToString("N0"), "All account roles"),
                new("Active users", model.ActiveUsers.ToString("N0"), "Enabled and not locked"),
                new("Staff accounts", model.UserRoles.GetValueOrDefault("Staff").ToString("N0"), "All Staff account statuses"),
                new("Client accounts", model.UserRoles.GetValueOrDefault("Client").ToString("N0"), "All Client account statuses")]);
        }
        if (!model.UnavailableSections.Contains("system overview"))
        {
            AddAttention(model, "Failed logins today (UTC)", model.FailedLogins, "/SystemActivity?type=LoginFailed&fromDate=" + DateTime.UtcNow.ToString("yyyy-MM-dd"), "bi-shield-exclamation");
            model.Metrics.Add(new("Archived records", (model.TrashedBookingsCount + model.ArchivedPackages).ToString("N0"), "Booking archives and archived packages"));
            model.Metrics.Add(new("System activity", model.RecentActivityCount.ToString("N0"), "Recorded in the last 24 hours"));
        }
        AddMessages(model);
        model.QuickActions = [
            new("User Management", "/UserManagement", "bi-people"),
            new("Packages", "/Services", "bi-box-seam"),
            new("Calendar Management", "/Calendar", "bi-calendar3"),
            new("Trash History", "/TrashHistory", "bi-trash"),
            new("System Activity", "/SystemActivity", "bi-activity"),
            new("Messages", "/Communications", "bi-chat-dots")];
        return View(model);
    }
}
