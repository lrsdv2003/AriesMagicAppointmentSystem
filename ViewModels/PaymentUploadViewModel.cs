using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace AriesMagicAppointmentSystem.ViewModels
{
    public class PaymentUploadViewModel
    {
        [Required]
        [Display(Name = "Booking")]
        public int BookingId { get; set; }

        [Required]
        [Range(0.01, 999999)]
        public decimal Amount { get; set; }

        [Required]
        [Display(Name = "Proof Image")]
        public IFormFile ProofImage { get; set; } = default!;

        public List<SelectListItem> Bookings { get; set; } = new();
    }
}