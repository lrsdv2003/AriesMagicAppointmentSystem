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

        [Required(ErrorMessage = "Open a fresh payment form before submitting your receipt.")]
        [Range(0, int.MaxValue)]
        public int? ExpectedLatestPaymentId { get; set; }

        public decimal FixedDownpaymentAmount { get; set; } = 2000;

        [Required]
        [RegularExpression("^(Minimum|Half|Full)$", ErrorMessage = "Choose one of the available payment options.")]
        public string PaymentOption { get; set; } = "Minimum";
        public decimal Amount { get; set; }
        public List<PaymentOptionViewModel> Options { get; set; } = new();

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

public sealed record PaymentOptionViewModel(string Key, string Name, decimal Amount, decimal Remaining, string Explanation);
