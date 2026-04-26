using System.ComponentModel.DataAnnotations;

namespace AriesMagicAppointmentSystem.ViewModels
{
    public class BookingStepOneViewModel
    {
        [Required]
        [Display(Name = "Type of Event")]
        public string EventType { get; set; } = string.Empty;

        [Required]
        public string Motif { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Date)]
        [Display(Name = "Date of Event")]
        public DateTime EventDate { get; set; }

        [Required]
        [DataType(DataType.Time)]
        [Display(Name = "Time of Event")]
        public TimeSpan StartTime { get; set; }

        [Required]
        [Display(Name = "Theme of the Party")]
        public string PartyTheme { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Party Venue")]
        public string PartyVenue { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Celebrant Name")]
        public string CelebrantName { get; set; } = string.Empty;

        [Range(1, 120)]
        public int Age { get; set; }

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