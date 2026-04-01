using System.ComponentModel.DataAnnotations;

namespace AriesMagicAppointmentSystem.Models
{
    public class Service
    {
        public int Id { get; set; }

        [Required]
        [StringLength(150)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [Range(0, 999999)]
        public decimal Price { get; set; }

        [Required]
        [Range(1, 24)]
        public int DurationInHours { get; set; }

        public bool IsArchived { get; set; } = false;

        public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
    }
}