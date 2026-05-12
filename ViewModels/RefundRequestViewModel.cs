using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace AriesMagicAppointmentSystem.ViewModels
{
    public class RefundRequestViewModel
    {
        [Required(ErrorMessage = "Please select a booking.")]
        [Display(Name = "Booking")]
        public int BookingId { get; set; }

        public decimal FixedRefundAmount { get; set; } = 2000m;

        [Required(ErrorMessage = "Please enter your GCash account name.")]
        [Display(Name = "GCash Account Name")]
        [StringLength(100)]
        public string GCashAccountName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter your GCash number.")]
        [Display(Name = "GCash Number")]
        [StringLength(20)]
        public string GCashNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please upload your payment proof.")]
        [Display(Name = "Payment Proof")]
        public IFormFile? PaymentProofImage { get; set; }

        [Display(Name = "Reason")]
        [StringLength(500)]
        public string? ClientReason { get; set; }

        public List<SelectListItem> Bookings { get; set; } = new();
    }
}