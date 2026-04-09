using System.ComponentModel.DataAnnotations;

namespace AriesMagicAppointmentSystem.ViewModels
{
    public class ForgotPasswordViewModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;
    }
}