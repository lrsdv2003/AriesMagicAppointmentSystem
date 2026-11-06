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

        public DateTime? CancelledAtUtc { get; set; }
        [MaxLength(450)] public string? CancelledByUserId { get; set; }
        [MaxLength(20)] public string? CancellationActorRole { get; set; }
        [MaxLength(1000)] public string? CancellationReason { get; set; }
        [MaxLength(40)] public string? CancellationSource { get; set; }
        [MaxLength(40)] public string? CancellationPreviousStatus { get; set; }
        public int? CancellationPaymentId { get; set; }
        public DateTime? CancellationRequestAtUtc { get; set; }
        [MaxLength(450)] public string? CancellationRequestByUserId { get; set; }
        [MaxLength(1000)] public string? CancellationRequestReason { get; set; }
        public int? CancellationRequestMessageId { get; set; }
        public bool RefundEligible { get; set; }
        [MaxLength(60)] public string? RefundEligibilityReason { get; set; }
        public int? RefundEligibilityPaymentId { get; set; }

        [MaxLength(40)] public string? AcceptedTermsVersion { get; set; }
        public DateTime? TermsAcceptedAtUtc { get; set; }

        public string? ReopenReason { get; set; }
        public string? InternalNotes { get; set; }

        // Trash History fields
        public TrashReason? TrashReason { get; set; }
        public string? TrashNotes { get; set; }
        public DateTime? ArchivedAt { get; set; }
        public string? AssignedStaffName { get; set; }
        public int? OriginalBookingId { get; set; }

        // Event Details
        [Required]
        public string EventType { get; set; } = string.Empty;

        [Required]
        public string Motif { get; set; } = string.Empty;

        public string? PartyTheme { get; set; }

        [Required]
        public string PartyVenue { get; set; } = string.Empty;

        public string? CelebrantName { get; set; }

        // Smart Venue Distance & Serviceability
        public double? VenueLatitude { get; set; }
        public double? VenueLongitude { get; set; }
        public double? DistanceKm { get; set; }
        public decimal TravelFee { get; set; }
        public bool IsServiceable { get; set; } = true;
        public string? ServiceZone { get; set; }
        public bool RequiresManualReview { get; set; }
        [MaxLength(20)] public string? VenueSelectionMethod { get; set; }
        [MaxLength(32)] public string? VenueVerificationStatus { get; set; }
        [MaxLength(32)] public string? TravelFeeStatus { get; set; }
        [MaxLength(500)] public string? VenueDirections { get; set; }
        [MaxLength(500)] public string? VenueManualDetails { get; set; }
        public DateTime? VenueStaffReviewedAtUtc { get; set; }
        public DateTime? TravelQuoteAcceptedAtUtc { get; set; }

        [Range(1, 120)]
        public int? Age { get; set; }

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
