using System.Collections.Generic;

namespace AriesMagicAppointmentSystem.ViewModels
{
    public class ReportDashboardViewModel
    {
        public int TotalBookings { get; set; }
        public int ConfirmedBookings { get; set; }
        public int ExpiredBookings { get; set; }
        public int CancelledBookings { get; set; }

        public decimal TotalRevenue { get; set; }

        public double ConfirmationRate { get; set; }
        public decimal AverageBookingValue { get; set; }
        public double AveragePaymentProcessingDays { get; set; }
        public double CustomerRetentionRate { get; set; }

        public List<string> MonthlyLabels { get; set; } = new();
        public List<int> MonthlyBookingCounts { get; set; } = new();
        public List<decimal> MonthlyRevenue { get; set; } = new();

        public int PendingCount { get; set; }
        public int VerifiedCount { get; set; }
        public int RejectedCount { get; set; }

        // History module integration - completed bookings are counted here instead of inside
        // the active booking totals above.
        public int UpcomingEvents { get; set; }
        public int TodaysEvents { get; set; }
        public int HistoryCount { get; set; }
        public int CompletedThisMonth { get; set; }
        public int CompletedThisYear { get; set; }
        public int LifetimeEvents { get; set; }
    }
}