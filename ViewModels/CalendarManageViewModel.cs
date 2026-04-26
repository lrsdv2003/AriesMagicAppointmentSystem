using AriesMagicAppointmentSystem.Models;

namespace AriesMagicAppointmentSystem.ViewModels
{
    public class CalendarManageViewModel
    {
        public int MaxBookingsPerDay { get; set; } = 3;

        public DateTime? BlockDate { get; set; }
        public string? BlockReason { get; set; }

        public DateTime? LimitDate { get; set; }
        public int? LimitMaxBookings { get; set; }

        public List<BlockedDate> BlockedDates { get; set; } = new();
        public List<DateBookingLimit> DateBookingLimits { get; set; } = new();
    }
}