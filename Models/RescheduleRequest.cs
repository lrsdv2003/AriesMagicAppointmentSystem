using System.ComponentModel.DataAnnotations;

namespace AriesMagicAppointmentSystem.Models
{
    public class RescheduleRequest
    {
        public int Id { get; set; }

        [Required]
        public int BookingId { get; set; }
        public Booking? Booking { get; set; }

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

        public string? AdminRemarks { get; set; }
    }
}