using AriesMagicAppointmentSystem.Models;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AriesMagicAppointmentSystem.ViewModels;
public static class AdminActivityPresentation
{
    public static string Label(object value) => Regex.Replace(value.ToString() == "RejectedByAdmin" ? "Declined during review" : value.ToString() ?? "", "([a-z])([A-Z])", "$1 $2").Replace("Service", "Package");
    public static string Category(SystemActivityType type) => type.ToString() switch
    {
        var n when n.StartsWith("User") || n.StartsWith("Role") => "User Management",
        var n when n.StartsWith("Service") => "Package",
        var n when n.StartsWith("Payment") || n.StartsWith("Ocr") => "Payment",
        var n when n.StartsWith("Refund") => "Refund",
        var n when n.StartsWith("Booking") || n.StartsWith("Reschedule") => "Booking",
        "CalendarModified" => "Calendar",
        "LoginFailed" => "Authentication",
        _ => "System"
    };
    public static string ActorRole(SystemActivity activity)
    {
        try
        {
            using var json = JsonDocument.Parse(activity.MetadataJson ?? "{}");
            return json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("ActorRole", out var role) ? role.GetString() ?? "Not recorded" : "Not recorded";
        }
        catch (JsonException) { return "Not recorded"; }
    }
    public static string Result(SystemActivityType type) => type == SystemActivityType.LoginFailed ? "Failed" : "Success";
}
