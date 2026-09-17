using System.ComponentModel.DataAnnotations;

namespace AriesMagicAppointmentSystem.Models
{
    public class RescheduleRequest
    {
        public int Id { get; set; }

        [Required]
        public int BookingId { get; set; }
        public Booking? Booking { get; set; }

        // Immutable audit snapshot of the booking schedule when the client submitted this request.
        // The live booking schedule remains on Booking and is updated only after approval.
        public DateTime? OriginalDate { get; set; }
        public DateTime? OriginalStartTime { get; set; }
        public DateTime? OriginalEndTime { get; set; }

        [Required]
        public DateTime RequestedDate { get; set; }

        [Required]
        public DateTime RequestedStartTime { get; set; }

        [Required]
        public DateTime RequestedEndTime { get; set; }

        [Required]
        public string Reason { get; set; } = string.Empty;

        [Required]
        public string Status { get; set; } = RescheduleRequestStatus.Pending;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime? ReviewedAt { get; set; }

        [MaxLength(450)]
        public string? ReviewedByUserId { get; set; }

        [MaxLength(200)]
        public string? ReviewedByName { get; set; }

        [MaxLength(1000)]
        public string? InternalNote { get; set; }

        [MaxLength(1000)]
        public string? ClientFacingReason { get; set; }

        // Legacy field retained for older records created before Staff review/audit separation.
        public string? AdminRemarks { get; set; }
    }
}