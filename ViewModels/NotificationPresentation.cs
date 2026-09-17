using AriesMagicAppointmentSystem.Models;
namespace AriesMagicAppointmentSystem.ViewModels;
public record NotificationPresentation(string Label, string Accent, string Icon, string Action)
{
    public static NotificationPresentation From(Notification notification)
    {
        var title = notification.Title ?? "";
        var path = (notification.Link ?? "").Split('?')[0];
        bool Has(string value) => title.Contains(value, StringComparison.OrdinalIgnoreCase);
        bool Route(string value) => path.Contains(value, StringComparison.OrdinalIgnoreCase);
        var category = Has("refund") || Route("Refund") ? ("REFUND", "refund", "bi-arrow-counterclockwise")
            : Has("reschedule") || Route("Reschedule") ? ("RESCHEDULE", "reschedule", "bi-arrow-left-right")
            : Has("payment") || Has("balance") || Route("/Payments/") ? ("PAYMENT", "payment", "bi-credit-card")
            : Has("calendar") || Route("/Calendar/") ? ("CALENDAR", "calendar", "bi-calendar3")
            : Has("message") || Route("/Communications/") ? ("MESSAGE", "message", "bi-chat-dots")
            : Has("booking") || Route("/Bookings/") ? ("BOOKING", "booking", "bi-journal-check")
            : ("SYSTEM", "system", "bi-info-circle");
        var action = Route("/Payments/RefundReview") ? "Review Refund"
            : Route("/Payments/Verify") ? "Review Payment"
            : Route("/RescheduleRequests/") ? "View Request"
            : Route("/Communications/") ? "Open Message"
            : Route("/Bookings/Details") ? "View Booking"
            : Route("/Calendar/") ? "View Calendar" : "View Details";
        return new(category.Item1, category.Item2, category.Item3, action);
    }
    public static string TimeLabel(DateTime createdAt, DateTime now)
    {
        var elapsed = now - createdAt;
        if (elapsed.TotalMinutes < 1) return "Just now";
        if (elapsed.TotalHours < 1) return $"{(int)elapsed.TotalMinutes}m ago";
        if (createdAt.Date == now.Date) return $"{(int)elapsed.TotalHours}h ago";
        return createdAt.Date == now.Date.AddDays(-1) ? "Yesterday" : createdAt.ToString("MMM d, yyyy");
    }
}
