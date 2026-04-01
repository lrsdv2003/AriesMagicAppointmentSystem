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

        [Required]
        public string Status { get; set; } = PaymentStatus.Pending;
        public DateTime UploadedAt { get; set; } = DateTime.Now;

        public DateTime? VerifiedAt { get; set; }

        public string? RejectionReason { get; set; }
    }
}