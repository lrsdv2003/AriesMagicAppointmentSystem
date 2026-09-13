using System.ComponentModel.DataAnnotations;

namespace AriesMagicAppointmentSystem.Models
{
    public class Message
    {
        public int Id { get; set; }
        public int ConversationId { get; set; }
        public Conversation? Conversation { get; set; }

        [Required, MaxLength(450)]
        public string SenderId { get; set; } = string.Empty;
        public ApplicationUser? Sender { get; set; }

        [Required, MaxLength(4000)]
        public string MessageContent { get; set; } = string.Empty;

        public DateTime SentAt { get; set; } = DateTime.UtcNow;

        [Required, MaxLength(50)]
        public string MessageType { get; set; } = MessageTypes.Text;

        [MaxLength(100)]
        public string? RequestType { get; set; }

        [MaxLength(50)]
        public string? RequestStatus { get; set; }

        [MaxLength(500)]
        public string? ActionLink { get; set; }
    }

    public static class MessageTypes
    {
        public const string Text = "Text";
        public const string ActionRequest = "ActionRequest";
    }

    public static class MessageRequestStatuses
    {
        public const string Open = "Open";
        public const string Completed = "Completed";
    }
}
