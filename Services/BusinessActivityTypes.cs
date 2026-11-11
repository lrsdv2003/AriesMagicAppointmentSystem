using AriesMagicAppointmentSystem.Models;
namespace AriesMagicAppointmentSystem.Services;
public static class BusinessActivityTypes
{
    public static readonly SystemActivityType[] All = Enum.GetValues<SystemActivityType>().Where(t=>
        t.ToString().StartsWith("User",StringComparison.Ordinal)||t.ToString().StartsWith("Role",StringComparison.Ordinal)||
        t.ToString().StartsWith("Service",StringComparison.Ordinal)||t.ToString().StartsWith("Booking",StringComparison.Ordinal)||
        t.ToString().StartsWith("Payment",StringComparison.Ordinal)||t.ToString().StartsWith("Refund",StringComparison.Ordinal)||
        t.ToString().StartsWith("Reschedule",StringComparison.Ordinal)||t is SystemActivityType.CalendarModified or SystemActivityType.SettingsChanged or SystemActivityType.FeedbackModerated).ToArray();
}
