using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace AriesMagicAppointmentSystem.ViewModels
{
    public class BookingCreateViewModel
    {
        [Required]
        [Display(Name = "Service")]
        public int ServiceId { get; set; }

        [Required]
        [DataType(DataType.Date)]
        [Display(Name = "Event Date")]
        public DateTime EventDate { get; set; }

        [Required]
        [DataType(DataType.Time)]
        [Display(Name = "Start Time")]
        public TimeSpan StartTime { get; set; }

        public List<SelectListItem> Services { get; set; } = new();
    }
}