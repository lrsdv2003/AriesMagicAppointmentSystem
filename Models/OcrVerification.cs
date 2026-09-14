using System.ComponentModel.DataAnnotations;

namespace AriesMagicAppointmentSystem.Models
{
    public class OcrVerification
    {
        public int Id { get; set; }
        public int? PaymentId { get; set; }
        public Payment? Payment { get; set; }
        public int? RefundRequestId { get; set; }
        public RefundRequest? RefundRequest { get; set; }
        [Required, MaxLength(60)] public string VerificationPurpose { get; set; } = OcrVerificationPurposes.PaymentProof;

        public decimal? ExtractedAmount { get; set; }
        [MaxLength(100)] public string? ExtractedReferenceNumber { get; set; }
        [MaxLength(200)] public string? ExtractedSender { get; set; }
        [MaxLength(200)] public string? ExtractedReceiver { get; set; }
        public DateTime? ExtractedDate { get; set; }
        [MaxLength(40)] public string? ExtractedTime { get; set; }
        [MaxLength(80)] public string? ExtractedStatus { get; set; }
        [MaxLength(80)] public string? ExtractedPaymentMethod { get; set; }
        public decimal? OcrConfidence { get; set; }

        [Required, MaxLength(80)]
        public string VerificationResult { get; set; } = OcrVerificationResults.ManualReviewRequired;
        public bool IsDuplicateReference { get; set; }
        public int? DuplicatePaymentId { get; set; }
        [MaxLength(2000)] public string? WarningSummary { get; set; }
        public string? RawText { get; set; }
        [MaxLength(1000)] public string? ProcessingError { get; set; }
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
    }

    public static class OcrVerificationResults
    {
        public const string LikelyMatch = "Likely Match";
        public const string PartialMatch = "Partial Match";
        public const string MismatchDetected = "Mismatch Detected";
        public const string OcrFailed = "OCR Failed";
        public const string ManualReviewRequired = "Manual Review Required";
    }
}

namespace AriesMagicAppointmentSystem.Models
{
    public static class OcrVerificationPurposes
    {
        public const string PaymentProof = "PaymentProof";
        public const string RefundSupport = "RefundSupport";
        public const string RefundConfirmation = "RefundConfirmation";
    }
}
