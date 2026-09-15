using AriesMagicAppointmentSystem.Models;

namespace AriesMagicAppointmentSystem.ViewModels
{
    public class ReportDashboardViewModel
    {
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }

        public int TotalBookings { get; set; }
        public int ConfirmedBookings { get; set; }
        public int CompletedBookings { get; set; }
        public int CancelledBookings { get; set; }
        public int ExpiredBookings { get; set; }

        public decimal TotalRevenue { get; set; }
        public decimal TotalBookingValue { get; set; }
        public decimal TotalCollectedRevenue { get; set; }
        public decimal TotalDownPaymentsCollected { get; set; }
        public decimal OutstandingReceivables { get; set; }
        public decimal CompletedBookingValue { get; set; }
        public decimal CompletedCollectedRevenue { get; set; }
        public decimal CompletedOutstandingReceivables { get; set; }
        public decimal RefundsIssued { get; set; }
        public decimal NetCollectedRevenue { get; set; }
        public int FullyPaidBookings { get; set; }
        public int PartiallyPaidBookings { get; set; }
        public int UnpaidBookings { get; set; }

        public int PendingCount { get; set; }
        public int VerifiedCount { get; set; }
        public int RejectedCount { get; set; }

        public double ConfirmationRate { get; set; }
        public decimal AverageBookingValue { get; set; }
        public double AveragePaymentProcessingDays { get; set; }
        public double CustomerRetentionRate { get; set; }

        public List<string> MonthlyLabels { get; set; } = new();
        public List<int> MonthlyBookingCounts { get; set; } = new();
        public List<decimal> MonthlyRevenue { get; set; } = new();

        public List<string> PackageLabels { get; set; } = new();
        public List<int> PackageBookingCounts { get; set; } = new();

        public List<Booking> BookingRecords { get; set; } = new();
        public Dictionary<int, BookingFinancialSummaryViewModel> BookingFinancials { get; set; } = new();
    }
}
