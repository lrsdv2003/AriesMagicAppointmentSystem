using System.ComponentModel.DataAnnotations;

namespace AriesMagicAppointmentSystem.ViewModels
{
    public class BookingStepOneViewModel
    {
        [Required]
        [Display(Name = "Type of Event")]
        public string EventType { get; set; } = string.Empty;

        // Optional; event-specific relevance is enforced by BookingEventDetailsPolicy.
        public string? Motif { get; set; }

        [Required]
        [DataType(DataType.Date)]
        [Display(Name = "Date of Event")]
        public DateTime EventDate { get; set; }

        [Required]
        [DataType(DataType.Time)]
        [Display(Name = "Time of Event")]
        public TimeSpan StartTime { get; set; }

        [Display(Name = "Theme of the Party")]
        public string? PartyTheme { get; set; }

        [Required]
        [Display(Name = "Party Venue")]
        public string PartyVenue { get; set; } = string.Empty;

        public double? VenueLatitude { get; set; }
        public double? VenueLongitude { get; set; }
        public double? DistanceKm { get; set; }
        public decimal TravelFee { get; set; }
        public bool IsServiceable { get; set; }
        public string? ServiceZone { get; set; }
        public bool RequiresManualReview { get; set; }
        [MaxLength(20)] public string VenueSelectionMethod { get; set; } = "Search";
        [MaxLength(180)] public string? ManualStreet { get; set; }
        [MaxLength(100)] public string? ManualBarangay { get; set; }
        [MaxLength(100)] public string? ManualCity { get; set; }
        [MaxLength(100)] public string? ManualProvince { get; set; }
        [MaxLength(12)] public string? ManualPostalCode { get; set; }
        [MaxLength(120)] public string? ManualVenueName { get; set; }
        [MaxLength(500)] public string? VenueDirections { get; set; }
        public bool CustomerConfirmedLocation { get; set; }

        [Display(Name = "Celebrant Name")]
        public string? CelebrantName { get; set; }

        [Range(1, 120)]
        public int? Age { get; set; }

        [Range(1, 10000)]
        [Display(Name = "# of Pax")]
        public int PaxCount { get; set; }

        [Required]
        [Display(Name = "Contact Person")]
        public string ContactPerson { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Contact Number")]
        public string ContactNumber { get; set; } = string.Empty;
    }
}