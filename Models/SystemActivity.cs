namespace AriesMagicAppointmentSystem.Models
{
    public enum SystemActivityType
    {
        UserCreated,
        UserDisabled,
        UserEnabled,
        ServiceCreated,
        ServiceUpdated,
        ServiceArchived,
        ServiceRestored,
        CalendarModified,
        SettingsChanged,
        BookingArchived,
        BookingDeleted,
        PaymentVerified,
        PaymentRejected,
        RefundApproved,
        RefundRejected,
        RescheduleApproved,
        RescheduleRejected,
        NotificationSent,
        LoginFailed,
        RoleAssigned,
        RoleRemoved
    }

    public class SystemActivity
    {
        public int Id { get; set; }

        public SystemActivityType Type { get; set; }

        public string Description { get; set; } = string.Empty;

        public string PerformedByUserId { get; set; } = string.Empty;

        public string? PerformedByUserName { get; set; }

        public string? AffectedRecordId { get; set; }

        public string? AffectedRecordType { get; set; }

        public string? MetadataJson { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}