namespace AriesMagicAppointmentSystem.Models
{
    public class TrashHistory
    {
        public int Id { get; set; }

        public int OriginalBookingId { get; set; }
        public string BookingCode { get; set; } = string.Empty;

        public string ClientName { get; set; } = string.Empty;
        public string ClientEmail { get; set; } = string.Empty;
        public string ClientPhone { get; set; } = string.Empty;

        public string PackageName { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty;
        public string PartyVenue { get; set; } = string.Empty;
        public string PartyTheme { get; set; } = string.Empty;
        public string CelebrantName { get; set; } = string.Empty;
        public int? Age { get; set; }
        public int PaxCount { get; set; }
        public string ContactPerson { get; set; } = string.Empty;
        public string ContactNumber { get; set; } = string.Empty;

        public DateTime EventDate { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }

        public decimal BasePrice { get; set; }
        public decimal FinalPrice { get; set; }
        public decimal RequiredDownpayment { get; set; }

        public TrashReason Reason { get; set; }
        public string? ReasonNotes { get; set; }

        public string? AssignedStaffId { get; set; }
        public string? AssignedStaffName { get; set; }

        public string PaymentStatus { get; set; } = "No Payment";

        public DateTime CreatedAt { get; set; }
        public DateTime ArchivedAt { get; set; } = DateTime.UtcNow;
    }
}