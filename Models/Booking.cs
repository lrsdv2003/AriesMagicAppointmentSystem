using System.ComponentModel.DataAnnotations;

namespace AriesMagicAppointmentSystem.Models
{
    public class Booking
    {
        
        public int Id { get; set; }

        [Required]
        public int ClientId { get; set; }
        public User? Client { get; set; }

        [Required]
        public int ServiceId { get; set; }
        public Service? Service { get; set; }

        [Required]
        public DateTime EventDate { get; set; }

        [Required]
        public DateTime StartTime { get; set; }

        [Required]
        public DateTime EndTime { get; set; }

        [Required]
        public string Status { get; set; } = BookingStatus.Pending;
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public bool IsCompletedLocked { get; set; } = false;

        public string? ReopenReason { get; set; }
        public string? InternalNotes { get; set; }

        public ICollection<Payment> Payments { get; set; } = new List<Payment>();
        public ICollection<BookingTimeline> TimelineEvents { get; set; } = new List<BookingTimeline>();
        public ICollection<RescheduleRequest> RescheduleRequests { get; set; } = new List<RescheduleRequest>();
    }
}