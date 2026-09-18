using AriesMagicAppointmentSystem.Models;

namespace AriesMagicAppointmentSystem.ViewModels
{
    public class RoleDashboardViewModel
    {
        public string DisplayName { get; set; } = "";
        public DateTime AsOf { get; set; } = DateTime.Now;
        public HashSet<string> UnavailableSections { get; set; } = new();
        public List<DashboardShortcut> Attention { get; set; } = new();
        public List<DashboardShortcut> QuickActions { get; set; } = new();
        public List<DashboardMetric> Metrics { get; set; } = new();
        public List<SystemActivity> RecentActivity { get; set; } = new();
        public List<Booking> TodayEvents { get; set; } = new();
        public List<RefundRequest> RecentRefundRequests { get; set; } = new();
        public List<DashboardTrendPoint> BookingTrend { get; set; } = new();
        public Dictionary<string, int> UserRoles { get; set; } = new();
        public int TodayBookings { get; set; }
        public int UnreadConversations { get; set; }
        public int ActiveUsers { get; set; }
        public int InactiveUsers { get; set; }
        public int UnverifiedUsers { get; set; }
        public int LockedUsers { get; set; }
        public int FailedLogins { get; set; }
        public int RecentActivityCount { get; set; }
        public decimal BookingValue { get; set; }
        public decimal CompletedRefunds { get; set; }
        public string MostBookedPackage { get; set; } = "No bookings yet";
        public string RoleName { get; set; } = string.Empty;

        public int TotalBookings { get; set; }
        public int PendingBookings { get; set; }
        public int ConfirmedBookings { get; set; }
        public int CompletedBookings { get; set; }
        public int UpcomingBookings { get; set; }

        public int PendingPayments { get; set; }
        public int PendingRefunds { get; set; }
        public int RefundProofsAwaitingVerification { get; set; }
        public int PaymentsAdditionalEvidence { get; set; }
        public int PendingReschedules { get; set; }

        public int ActiveClients { get; set; }
        public int ActiveStaff { get; set; }
        public int ActivePackages { get; set; }
        public int ArchivedPackages { get; set; }
        public int BlockedDates { get; set; }

        public decimal VerifiedRevenue { get; set; }
        public decimal CurrentMonthRevenue { get; set; }
        public decimal OutstandingReceivables { get; set; }
        public decimal NetCollectedRevenue { get; set; }

        public int TotalUsers { get; set; }
        public int TrashedBookingsCount { get; set; }

        public List<Booking> UpcomingEvents { get; set; } = new();
        public List<Booking> RecentBookings { get; set; } = new();
        public List<Booking> RecentBookingRequests { get; set; } = new();
        public List<Payment> RecentPaymentsToVerify { get; set; } = new();
        public List<RescheduleRequest> RecentRescheduleRequests { get; set; } = new();
    }
}


namespace AriesMagicAppointmentSystem.ViewModels
{
    public record DashboardShortcut(string Label, string Href, string Icon, string? Detail = null);
    public record DashboardMetric(string Label, string Value, string? Context = null);
    public record DashboardTrendPoint(string Label, int Count);
}
