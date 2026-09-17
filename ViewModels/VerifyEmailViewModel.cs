using System.ComponentModel.DataAnnotations;

namespace AriesMagicAppointmentSystem.ViewModels
{
    public class VerifyEmailViewModel
    {
        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter the 6-digit verification code.")]
        [RegularExpression(@"^\d{6}$", ErrorMessage = "Enter a valid 6-digit verification code.")]
        [Display(Name = "Verification Code")]
        public string Code { get; set; } = string.Empty;

        public string MaskedEmail { get; set; } = string.Empty;
        public DateTime? ExpiresAtUtc { get; set; }
        public int ResendSecondsRemaining { get; set; }
        public bool HasActiveCode { get; set; }
        public bool AttemptsLocked { get; set; }
    }
}
