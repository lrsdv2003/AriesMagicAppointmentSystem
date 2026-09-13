using System.ComponentModel.DataAnnotations;

namespace AriesMagicAppointmentSystem.Models
{
    public class Conversation
    {
        public int Id { get; set; }

        [Required, MaxLength(50)]
        public string ConversationType { get; set; } = ConversationTypes.InternalDirect;

        [MaxLength(200)]
        public string? Title { get; set; }

        public int? BookingId { get; set; }
        public Booking? Booking { get; set; }

        [Required, MaxLength(450)]
        public string CreatedByUserId { get; set; } = string.Empty;
        public ApplicationUser? CreatedByUser { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public bool IsClosed { get; set; }

        public ICollection<ConversationParticipant> Participants { get; set; } = new List<ConversationParticipant>();
        public ICollection<Message> Messages { get; set; } = new List<Message>();
    }

    public static class ConversationTypes
    {
        public const string ClientSupport = "ClientSupport";
        public const string Booking = "Booking";
        public const string InternalDirect = "InternalDirect";
        public const string InternalGroup = "InternalGroup";
        public const string OperationalRequest = "OperationalRequest";
    }
}
