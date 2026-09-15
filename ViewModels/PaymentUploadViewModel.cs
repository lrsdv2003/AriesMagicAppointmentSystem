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

        [Required]
        [Range(typeof(decimal), "0.01", "999999999", ErrorMessage = "Enter a valid payment amount.")]
        [Display(Name = "Payment Amount")]
        public decimal Amount { get; set; } = 2000;

        public string GCashQrPath { get; set; } = "/images/gcash-qr.jpeg";

        [Required]
        [Display(Name = "Payment Method")]
        public string PaymentMethod { get; set; } = "GCash";

        [Required]
        [Display(Name = "Proof Image")]
        public IFormFile ProofImage { get; set; } = default!;

        public List<SelectListItem> Bookings { get; set; } = new();
    }
}