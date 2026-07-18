using System.ComponentModel.DataAnnotations;

namespace AriesMagicAppointmentSystem.Models
{
    public class TrashHistory
    {
        public int Id { get; set; }

        public int BookingId { get; set; }
        public Booking? Booking { get; set; }

        public string ClientName { get; set; } = string.Empty;
        public string ClientEmail { get; set; } = string.Empty;
        public string ClientPhone { get; set; } = string.Empty;

        public string PackageName { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty;

        public DateTime EventDate { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }

        public decimal BasePrice { get; set; }
        public decimal FinalPrice { get; set; }
        public decimal RequiredDownpayment { get; set; }

        public TrashReason Reason { get; set; }
        public string? ReasonNotes { get; set; }

        public string PaymentStatus { get; set; } = string.Empty;

        public string? AssignedStaffId { get; set; }
        public string? AssignedStaffName { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime ArchivedAt { get; set; } = DateTime.UtcNow;
    }
}