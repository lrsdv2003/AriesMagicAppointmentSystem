using System.ComponentModel.DataAnnotations;
namespace AriesMagicAppointmentSystem.ViewModels;
public sealed class InternalProfileViewModel
{
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "";
    public string Status { get; set; } = "";
    public bool Verified { get; set; }
    public bool HasPhoto { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public List<string> PasswordRules { get; set; } = new();
    public InternalPersonalInput Personal { get; set; } = new();
    public InternalPasswordInput Password { get; set; } = new();
}
public sealed class InternalPersonalInput
{
    [Required, StringLength(100, MinimumLength = 2), Display(Name = "Full name")]
    public string FullName { get; set; } = "";
    [RegularExpression(@"^09\d{9}$", ErrorMessage = "Use an 11-digit number starting with 09."), Display(Name = "Contact number")]
    public string? PhoneNumber { get; set; }
}
public sealed class InternalPasswordInput
{
    [Required, DataType(DataType.Password), Display(Name = "Current password")]
    public string CurrentPassword { get; set; } = "";
    [Required, DataType(DataType.Password), Display(Name = "New password")]
    public string NewPassword { get; set; } = "";
    [Required, DataType(DataType.Password), Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match."), Display(Name = "Confirm new password")]
    public string ConfirmPassword { get; set; } = "";
}
