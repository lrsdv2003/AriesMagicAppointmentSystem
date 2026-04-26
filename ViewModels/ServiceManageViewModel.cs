using System.ComponentModel.DataAnnotations;

namespace AriesMagicAppointmentSystem.ViewModels
{
    public class ServiceManageViewModel
    {
        public int Id { get; set; }

        [Required]
        [Display(Name = "Package Name")]
        public string Name { get; set; } = string.Empty;

        [Required]
        [Range(0, 999999)]
        [Display(Name = "Base Price")]
        public decimal Price { get; set; }

        [Required]
        [Range(1, 24)]
        [Display(Name = "Duration (Hours)")]
        public int DurationInHours { get; set; }

        [Display(Name = "Package Description")]
        public string? Description { get; set; }

        public List<ServiceInclusionInputViewModel> Inclusions { get; set; } = new();
    }

    public class ServiceInclusionInputViewModel
    {
        public int Id { get; set; }

        [Required]
        [Display(Name = "Inclusion Name")]
        public string Name { get; set; } = string.Empty;

        [Range(0, 999999)]
        [Display(Name = "Deduction Amount")]
        public decimal DeductionAmount { get; set; }

        [Display(Name = "Removable by Client")]
        public bool IsRemovable { get; set; } = true;
    }
}