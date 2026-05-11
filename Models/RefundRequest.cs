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
    }
}