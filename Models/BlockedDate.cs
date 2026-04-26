using System.ComponentModel.DataAnnotations;

namespace AriesMagicAppointmentSystem.Models
{
    public class BlockedDate
    {
        public int Id { get; set; }

        [Required]
        public DateTime Date { get; set; }

        [StringLength(255)]
        public string? Reason { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}