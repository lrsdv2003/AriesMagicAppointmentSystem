using AriesMagicAppointmentSystem.Models;

namespace AriesMagicAppointmentSystem.ViewModels
{
    public class BookingManagementViewModel
    {
        public string? Search { get; set; }
        public string? BookingStatus { get; set; }
        public string? PaymentStatus { get; set; }
        public DateTime? EventDate { get; set; }
        public int? ServiceId { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 5;
        public int TotalCount { get; set; }
        public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
        public List<Service> AvailableServices { get; set; } = new();
        public List<BookingManagementRowViewModel> Bookings { get; set; } = new();
    }

    public class BookingManagementRowViewModel
    {
        public int Id { get; set; }
        public string BookingCode { get; set; } = string.Empty;
        public string ClientName { get; set; } = string.Empty;
        public string ServiceName { get; set; } = string.Empty;
        public DateTime EventDate { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string VenueAddress { get; set; } = string.Empty;
        public bool IsServiceable { get; set; }
        public string BookingStatus { get; set; } = string.Empty;
        public string PaymentStatus { get; set; } = "No Payment";
        public string? InternalNotes { get; set; }
        public double? DistanceKm { get; set; }
        public string? ServiceZone { get; set; }
        public decimal TravelFee { get; set; }
        public bool RequiresManualReview { get; set; }
        public string? TravelFeeStatus { get; set; }
    }
}
