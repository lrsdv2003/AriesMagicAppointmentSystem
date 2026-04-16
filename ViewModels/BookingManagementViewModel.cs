using AriesMagicAppointmentSystem.Models;

namespace AriesMagicAppointmentSystem.ViewModels
{
    public class BookingManagementViewModel
    {
        public string? Search { get; set; }
        public string? BookingStatus { get; set; }
        public string? PaymentStatus { get; set; }

        public List<BookingManagementRowViewModel> Bookings { get; set; } = new();
    }

    public class BookingManagementRowViewModel
    {
        public int Id { get; set; }
        public string BookingCode { get; set; } = string.Empty;
        public string ClientName { get; set; } = string.Empty;
        public string ServiceName { get; set; } = string.Empty;
        public DateTime EventDate { get; set; }
        public string BookingStatus { get; set; } = string.Empty;
        public string PaymentStatus { get; set; } = "No Payment";
        public string? InternalNotes { get; set; }
    }
}