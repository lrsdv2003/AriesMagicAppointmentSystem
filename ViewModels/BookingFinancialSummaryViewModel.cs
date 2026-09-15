namespace AriesMagicAppointmentSystem.ViewModels
{
    public static class BookingPaymentState
    {
        public const string Unpaid = "Unpaid";
        public const string DownPaymentPaid = "Down Payment Paid";
        public const string PartiallyPaid = "Partially Paid";
        public const string FullyPaid = "Fully Paid";
        public const string VerificationPending = "Payment Verification Pending";
        public const string AdditionalEvidenceRequired = "Additional Evidence Required";
        public const string PaymentRejected = "Payment Rejected";
    }

    public class PaymentLedgerItemViewModel
    {
        public int PaymentId { get; set; }
        public DateTime Date { get; set; }
        public string TransactionType { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Status { get; set; } = string.Empty;
        public string PaymentMethod { get; set; } = string.Empty;
        public string? ReferenceNumber { get; set; }
    }
    public class BookingFinancialSummaryViewModel
    {
        public int BookingId { get; set; }
        public decimal BookingTotal { get; set; }
        public decimal RequiredDownpayment { get; set; }
        public decimal TotalVerifiedPayments { get; set; }
        public decimal CompletedRefunds { get; set; }
        public decimal NetCollected { get; set; }
        public decimal RemainingBalance { get; set; }
        public decimal DownPaymentCollected { get; set; }
        public int VerifiedPaymentCount { get; set; }
        public bool HasPendingVerification { get; set; }
        public bool HasAdditionalEvidenceRequired { get; set; }
        public string PaymentStatus { get; set; } = BookingPaymentState.Unpaid;
        public List<PaymentLedgerItemViewModel> Ledger { get; set; } = new();

        public bool IsFullyPaid => RemainingBalance <= 0 && BookingTotal > 0;
        public bool HasOutstandingBalance => RemainingBalance > 0;
    }
}
