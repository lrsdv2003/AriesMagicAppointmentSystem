using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace AriesMagicAppointmentSystem.ViewModels
{
    public class RescheduleRequestCreateViewModel
    {
        [Required]
        [Display(Name = "Booking")]
        public int BookingId { get; set; }

        [Required]
        [DataType(DataType.Date)]
        [Display(Name = "Requested Date")]
        public DateTime RequestedDate { get; set; }

        [Required]
        [DataType(DataType.Time)]
        [Display(Name = "Requested Start Time")]
        public TimeSpan RequestedStartTime { get; set; }

        [Required]
        public string Reason { get; set; } = string.Empty;

        public List<SelectListItem> Bookings { get; set; } = new();
    }
}