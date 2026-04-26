using System.ComponentModel.DataAnnotations;

namespace AriesMagicAppointmentSystem.Models
{
    public class Booking
    {
        public int Id { get; set; }

        [Required]
        public int ClientId { get; set; }
        public User? Client { get; set; }

        public string? ApplicationUserId { get; set; }

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

        // Event Details
        [Required]
        public string EventType { get; set; } = string.Empty;

        [Required]
        public string Motif { get; set; } = string.Empty;

        [Required]
        public string PartyTheme { get; set; } = string.Empty;

        [Required]
        public string PartyVenue { get; set; } = string.Empty;

        [Required]
        public string CelebrantName { get; set; } = string.Empty;

        [Range(1, 120)]
        public int Age { get; set; }

        [Range(1, 10000)]
        public int PaxCount { get; set; }

        [Required]
        public string ContactPerson { get; set; } = string.Empty;

        [Required]
        public string ContactNumber { get; set; } = string.Empty;

        // Package Details
        [Required]
        public string PackageName { get; set; } = string.Empty;

        public decimal BasePrice { get; set; }

        public decimal FinalPrice { get; set; }

        public decimal RequiredDownpayment { get; set; } = 2000;

        public string? RemovedInclusionsJson { get; set; }

        public ICollection<Payment> Payments { get; set; } = new List<Payment>();
        public ICollection<BookingTimeline> TimelineEvents { get; set; } = new List<BookingTimeline>();
        public ICollection<RescheduleRequest> RescheduleRequests { get; set; } = new List<RescheduleRequest>();
    }
}