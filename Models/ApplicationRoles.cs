namespace AriesMagicAppointmentSystem.Models;
public static class ApplicationRoles
{
    public static readonly string[] All = ["Owner","Staff","Client"];
    public static bool IsActive(string role) => role is "Owner" or "Staff" or "Client";
}
