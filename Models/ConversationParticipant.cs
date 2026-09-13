using System.ComponentModel.DataAnnotations;

namespace AriesMagicAppointmentSystem.Models
{
    public class ConversationParticipant
    {
        public int Id { get; set; }
        public int ConversationId { get; set; }
        public Conversation? Conversation { get; set; }

        [Required, MaxLength(450)]
        public string UserId { get; set; } = string.Empty;
        public ApplicationUser? User { get; set; }

        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastReadAt { get; set; }
    }
}
