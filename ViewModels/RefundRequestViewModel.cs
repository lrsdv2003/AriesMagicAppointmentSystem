using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace AriesMagicAppointmentSystem.ViewModels
{
    public class RefundRequestViewModel
    {
        [Required]
        [Display(Name = "Booking")]
        public int BookingId { get; set; }

        public decimal FixedRefundAmount { get; set; } = 2000;

        [Required]
        [Display(Name = "GCash Account Name")]
        [StringLength(100)]
        public string GCashAccountName { get; set; } = string.Empty;

        [Required]
        [Display(Name = "GCash Number")]
        [StringLength(20)]
        public string GCashNumber { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Payment Proof")]
        public IFormFile PaymentProofImage { get; set; } = default!;

        [Display(Name = "Reason")]
        [StringLength(500)]
        public string? ClientReason { get; set; }

        public List<SelectListItem> Bookings { get; set; } = new();
    }
}