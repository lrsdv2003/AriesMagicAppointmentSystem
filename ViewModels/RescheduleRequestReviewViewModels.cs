using AriesMagicAppointmentSystem.Models;

namespace AriesMagicAppointmentSystem.ViewModels
{
    public class RescheduleRequestIndexViewModel
    {
        public List<RescheduleRequestReviewItemViewModel> Requests { get; set; } = new();
        public string StatusFilter { get; set; } = RescheduleRequestStatus.Pending;
        public string Search { get; set; } = string.Empty;
        public DateTime? RequestedDate { get; set; }
        public string SortBy { get; set; } = "newest";
        public int PendingCount { get; set; }
        public int ApprovedCount { get; set; }
        public int RejectedCount { get; set; }
    }

    public class RescheduleRequestReviewItemViewModel
    {
        public RescheduleRequest Request { get; set; } = new();
        public RescheduleAvailabilityViewModel Availability { get; set; } = new();
    }

    public class RescheduleRequestDetailsViewModel
    {
        public RescheduleRequest Request { get; set; } = new();
        public RescheduleAvailabilityViewModel Availability { get; set; } = new();
        public string? Decision { get; set; }
    }

    public class RescheduleAvailabilityViewModel
    {
        public bool IsAvailable { get; set; }
        public string State { get; set; } = "Available";
        public string Message { get; set; } = "Requested schedule is currently available.";
    }
}
