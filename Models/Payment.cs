using System.ComponentModel.DataAnnotations;

namespace AriesMagicAppointmentSystem.Models
{
    public class Payment
    {
        public int Id { get; set; }

        [Required]
        public int BookingId { get; set; }
        public Booking? Booking { get; set; }

        [Required]
        public decimal Amount { get; set; }

        [Required]
        public string ProofImagePath { get; set; } = string.Empty;

        [Required, MaxLength(80)]
        public string PaymentMethod { get; set; } = "GCash";

        [MaxLength(100)]
        public string? TransactionReference { get; set; }

        [Required]
        public string Status { get; set; } = PaymentStatus.Pending;
        public DateTime UploadedAt { get; set; } = DateTime.Now;

        public DateTime? VerifiedAt { get; set; }
        [MaxLength(450)] public string? VerifiedByUserId { get; set; }
        [MaxLength(200)] public string? VerifiedByUserName { get; set; }

        public string? RejectionReason { get; set; }
        [MaxLength(1000)] public string? ReviewerNote { get; set; }
        public ICollection<OcrVerification> OcrVerifications { get; set; } = new List<OcrVerification>();
    }
}