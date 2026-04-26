using System.ComponentModel.DataAnnotations;

namespace AriesMagicAppointmentSystem.Models
{
    public class SystemSetting
    {
        public int Id { get; set; }

        [Required]
        [Range(1, 20)]
        public int MaxBookingsPerDay { get; set; } = 3;
    }
}