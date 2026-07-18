using AriesMagicAppointmentSystem.Models;

namespace AriesMagicAppointmentSystem.ViewModels
{
    public class TrashHistoryFilterViewModel
    {
        public string? Search { get; set; }
        public TrashReason? Reason { get; set; }
        public string PaymentStatus { get; set; } = "All";
        public string? AssignedStaffId { get; set; }
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    public class TrashHistoryRowViewModel
    {
        public int Id { get; set; }
        public string BookingCode { get; set; } = string.Empty;
        public string ClientName { get; set; } = string.Empty;
        public string ClientPhone { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty;
        public string PackageName { get; set; } = string.Empty;
        public DateTime EventDate { get; set; }
        public TrashReason Reason { get; set; }
        public string ReasonDisplay => Reason.ToString().Replace("_", " ");
        public string? ReasonNotes { get; set; }
        public string PaymentStatus { get; set; } = string.Empty;
        public DateTime ArchivedAt { get; set; }
        public string? AssignedStaffName { get; set; }
    }

    public class TrashHistoryIndexViewModel
    {
        public TrashHistoryFilterViewModel Filters { get; set; } = new();
        public List<TrashHistoryRowViewModel> Bookings { get; set; } = new();
        public int TotalCount { get; set; }
        public int TotalPages { get; set; }
        public List<ApplicationUser> AvailableStaff { get; set; } = new();
        public List<string> AvailablePaymentStatuses { get; set; } = new() { "No Payment", "Pending", "Verified", "Rejected" };
    }

    public class TrashHistoryDetailsViewModel
    {
        public TrashHistoryDetailViewModel Trash { get; set; } = new();
    }

    public class TrashHistoryDetailViewModel
    {
        public int Id { get; set; }
        public string BookingCode { get; set; } = string.Empty;
        public string ClientName { get; set; } = string.Empty;
        public string ClientEmail { get; set; } = string.Empty;
        public string ClientPhone { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty;
        public string PackageName { get; set; } = string.Empty;
        public DateTime EventDate { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public string PartyVenue { get; set; } = string.Empty;
        public string? PartyTheme { get; set; }
        public string? CelebrantName { get; set; }
        public int? Age { get; set; }
        public int PaxCount { get; set; }
        public string ContactPerson { get; set; } = string.Empty;
        public string ContactNumber { get; set; } = string.Empty;
        public decimal BasePrice { get; set; }
        public decimal FinalPrice { get; set; }
        public decimal RequiredDownpayment { get; set; }
        public TrashReason Reason { get; set; }
        public string ReasonDisplay => Reason.ToString().Replace("_", " ");
        public string? ReasonNotes { get; set; }
        public string PaymentStatus { get; set; } = string.Empty;
        public DateTime ArchivedAt { get; set; }
        public string? AssignedStaffName { get; set; }
        public DateTime CreatedAt { get; set; }
        public int? OriginalBookingId { get; set; }
    }
}