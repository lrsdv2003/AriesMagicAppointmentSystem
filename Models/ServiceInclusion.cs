using System.ComponentModel.DataAnnotations;

namespace AriesMagicAppointmentSystem.Models
{
    public class ServiceInclusion
    {
        public int Id { get; set; }

        [Required]
        public int ServiceId { get; set; }
        public Service? Service { get; set; }

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