using System.ComponentModel.DataAnnotations;

namespace AriesMagicAppointmentSystem.Models
{
    public class DateBookingLimit
    {
        public int Id { get; set; }

        [Required]
        public DateTime Date { get; set; }

        [Required]
        [Range(0, 20)]
        public int MaxBookings { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}