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

        public decimal FixedDownpaymentAmount { get; set; } = 2000;

        public string GCashQrPath { get; set; } = "/images/gcash-qr.jpeg";

        [Required]
        [Display(Name = "Proof Image")]
        public IFormFile ProofImage { get; set; } = default!;

        public List<SelectListItem> Bookings { get; set; } = new();
    }
}