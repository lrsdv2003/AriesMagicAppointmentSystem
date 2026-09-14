using System.ComponentModel.DataAnnotations;

namespace AriesMagicAppointmentSystem.Models
{
    public class RefundRequest
    {
        public int Id { get; set; }

        [Required]
        public int BookingId { get; set; }
        public Booking? Booking { get; set; }

        [Required]
        public decimal Amount { get; set; } = 2000;

        public int? OriginalPaymentId { get; set; }
        public Payment? OriginalPayment { get; set; }
        public decimal? ApprovedAmount { get; set; }

        [Required]
        [StringLength(100)]
        public string GCashAccountName { get; set; } = string.Empty;

        [Required]
        [StringLength(20)]
        public string GCashNumber { get; set; } = string.Empty;

        [Required]
        public string PaymentProofImagePath { get; set; } = string.Empty;

        [StringLength(500)]
        public string? ClientReason { get; set; }

        [Required]
        public string Status { get; set; } = RefundStatus.Pending;

        public string? AdminRemarks { get; set; }

        public DateTime RequestedAt { get; set; } = DateTime.Now;

        public DateTime? ProcessedAt { get; set; }
        public string? RefundProofImagePath { get; set; }
        [MaxLength(100)] public string? RefundReferenceNumber { get; set; }
        public DateTime? RefundCompletedAt { get; set; }
        [MaxLength(450)] public string? ReviewedByUserId { get; set; }
        [MaxLength(200)] public string? ReviewedByUserName { get; set; }
        public ICollection<OcrVerification> OcrVerifications { get; set; } = new List<OcrVerification>();
    }
}