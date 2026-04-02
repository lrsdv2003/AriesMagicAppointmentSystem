using Microsoft.AspNetCore.Identity;

namespace AriesMagicAppointmentSystem.Models
{
    public class ApplicationUser : IdentityUser
    {
        public string FullName { get; set; } = string.Empty;
    }
}