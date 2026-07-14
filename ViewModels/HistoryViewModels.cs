using AriesMagicAppointmentSystem.Models;

namespace AriesMagicAppointmentSystem.ViewModels
{
    /// <summary>
    /// Filter/sort/search options accepted by History/Index. Kept as simple strings/nullable
    /// values so it binds directly from the query string on GET requests.
    /// </summary>
    public class HistoryFilterViewModel
    {
        public string? Search { get; set; }

        // "Today" | "ThisWeek" | "ThisMonth" | "Year" | "" (any)
        public string? DateRange { get; set; }

        // Used together with DateRange == "Year"
        public int? Year { get; set; }

        public DateTime? CompletedDateFrom { get; set; }
        public DateTime? CompletedDateTo { get; set; }

        public string? PaymentStatus { get; set; }
        public string? RefundStatus { get; set; }
        public int? ServiceId { get; set; }
        public string? EventType { get; set; }

        // "Newest" | "Oldest" | "HighestRevenue" | "LowestRevenue" | "ClientName" | "EventDate"
        public string? SortBy { get; set; }

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 15;
    }

    public class HistorySummaryViewModel
    {
        public int UpcomingEvents { get; set; }
        public int TodaysEvents { get; set; }
        public int HistoryCount { get; set; }
        public int CompletedThisMonth { get; set; }
        public int CompletedThisYear { get; set; }
        public int LifetimeEvents { get; set; }
        public decimal LifetimeRevenue { get; set; }
    }

    public class HistoryRowViewModel
    {
        public int Id { get; set; }
        public string BookingCode { get; set; } = string.Empty;
        public string ClientName { get; set; } = string.Empty;
        public string ClientPhone { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty;
        public string PackageName { get; set; } = string.Empty;
        public string ServiceName { get; set; } = string.Empty;
        public string Venue { get; set; } = string.Empty;
        public DateTime EventDate { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public int Guests { get; set; }
        public decimal FinalPrice { get; set; }
        public decimal AmountPaid { get; set; }
        public decimal RemainingBalance { get; set; }
        public string PaymentStatus { get; set; } = "No Payment";
        public string RefundStatus { get; set; } = "None";
        public DateTime? CompletedAt { get; set; }
    }

    public class HistoryIndexViewModel
    {
        public HistoryFilterViewModel Filters { get; set; } = new();
        public HistorySummaryViewModel Summary { get; set; } = new();

        public List<HistoryRowViewModel> Bookings { get; set; } = new();

        public int TotalCount { get; set; }
        public int TotalPages { get; set; }

        public List<Service> AvailableServices { get; set; } = new();
        public List<string> AvailableEventTypes { get; set; } = new();
    }

    public class HistoryTimelineItemViewModel
    {
        public string EventType { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class HistoryDetailsViewModel
    {
        public Booking Booking { get; set; } = null!;
        public string BookingCode { get; set; } = string.Empty;

        public decimal AmountPaid { get; set; }
        public decimal RemainingBalance { get; set; }
        public string PaymentStatus { get; set; } = "No Payment";

        public RefundRequest? Refund { get; set; }
        public string RefundStatus { get; set; } = "None";

        public DateTime? CompletedAt { get; set; }

        public List<HistoryTimelineItemViewModel> Timeline { get; set; } = new();
    }
}
