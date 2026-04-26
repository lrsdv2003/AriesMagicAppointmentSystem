using System.ComponentModel.DataAnnotations;

namespace AriesMagicAppointmentSystem.ViewModels
{
    public class BookingStepTwoViewModel
    {
        // Step 1 Data
        public string EventType { get; set; } = string.Empty;
        public string Motif { get; set; } = string.Empty;
        public DateTime EventDate { get; set; }
        public TimeSpan StartTime { get; set; }
        public string PartyTheme { get; set; } = string.Empty;
        public string PartyVenue { get; set; } = string.Empty;
        public string CelebrantName { get; set; } = string.Empty;
        public int Age { get; set; }
        public int PaxCount { get; set; }
        public string ContactPerson { get; set; } = string.Empty;
        public string ContactNumber { get; set; } = string.Empty;

        // Step 2 Data
        [Required]
        [Display(Name = "Package")]
        public int ServiceId { get; set; }

        public string PackageName { get; set; } = string.Empty;
        public decimal BasePrice { get; set; }
        public decimal FinalPrice { get; set; }
        public decimal RequiredDownpayment { get; set; } = 2000;

        public List<ServiceOptionViewModel> AvailablePackages { get; set; } = new();
        public List<PackageInclusionSelectionViewModel> Inclusions { get; set; } = new();
        public List<int> RemovedInclusionIds { get; set; } = new();
    }

    public class ServiceOptionViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
    }

    public class PackageInclusionSelectionViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal DeductionAmount { get; set; }
        public bool IsRemovable { get; set; }
        public bool IsSelected { get; set; } = true;
    }
}