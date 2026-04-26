using System.ComponentModel.DataAnnotations;

namespace AriesMagicAppointmentSystem.Models
{
    public class Service
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

        public bool IsArchived { get; set; } = false;

        public ICollection<ServiceInclusion> Inclusions { get; set; } = new List<ServiceInclusion>();
    }
}