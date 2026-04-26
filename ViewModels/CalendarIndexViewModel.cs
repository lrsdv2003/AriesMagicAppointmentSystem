using AriesMagicAppointmentSystem.Models;

namespace AriesMagicAppointmentSystem.ViewModels
{
    public class CalendarIndexViewModel
    {
        public List<Booking> Bookings { get; set; } = new();
        public CalendarManageViewModel Manage { get; set; } = new();
    }
}