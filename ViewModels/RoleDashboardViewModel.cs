using AriesMagicAppointmentSystem.Models;

namespace AriesMagicAppointmentSystem.ViewModels
{
    public class RoleDashboardViewModel
    {
        public string RoleName { get; set; } = string.Empty;

        public int TotalBookings { get; set; }
        public int PendingBookings { get; set; }
        public int ConfirmedBookings { get; set; }
        public int CompletedBookings { get; set; }
        public int UpcomingBookings { get; set; }

        public int PendingPayments { get; set; }
        public int PendingRefunds { get; set; }
        public int PendingReschedules { get; set; }

        public int ActiveClients { get; set; }
        public int ActiveStaff { get; set; }
        public int ActivePackages { get; set; }
        public int ArchivedPackages { get; set; }
        public int BlockedDates { get; set; }

        public decimal VerifiedRevenue { get; set; }
        public decimal CurrentMonthRevenue { get; set; }

        public int TotalUsers { get; set; }
        public int TrashedBookingsCount { get; set; }

        public List<Booking> UpcomingEvents { get; set; } = new();
        public List<Booking> RecentBookings { get; set; } = new();
        public List<Booking> RecentBookingRequests { get; set; } = new();
        public List<Payment> RecentPaymentsToVerify { get; set; } = new();
        public List<RescheduleRequest> RecentRescheduleRequests { get; set; } = new();
    }
}
