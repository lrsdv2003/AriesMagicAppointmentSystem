using AriesMagicAppointmentSystem.Models;

namespace AriesMagicAppointmentSystem.ViewModels
{
    public class CommunicationCenterViewModel
    {
        public List<ConversationListItemViewModel> Conversations { get; set; } = new();
        public ConversationDetailViewModel? SelectedConversation { get; set; }
        public List<CommunicationUserOptionViewModel> InternalUsers { get; set; } = new();
        public string Filter { get; set; } = "all";
        public string Search { get; set; } = string.Empty;
        public bool IsInternalUser { get; set; }
        public int UnreadConversationCount { get; set; }
    }

    public class ConversationListItemViewModel
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string ConversationType { get; set; } = string.Empty;
        public int? BookingId { get; set; }
        public string LatestMessage { get; set; } = "No messages yet";
        public DateTime LastActivityAt { get; set; }
        public int UnreadCount { get; set; }
        public string Initials { get; set; } = "AM";
        public bool HasOpenRequest { get; set; }
    }

    public class ConversationDetailViewModel
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string ConversationType { get; set; } = string.Empty;
        public string ParticipantSummary { get; set; } = string.Empty;
        public int? BookingId { get; set; }
        public BookingConversationContextViewModel? Booking { get; set; }
        public List<CommunicationParticipantViewModel> Participants { get; set; } = new();
        public List<CommunicationMessageViewModel> Messages { get; set; } = new();
        public bool CanSend { get; set; } = true;
        public bool IsInternal { get; set; }
    }

    public class BookingConversationContextViewModel
    {
        public int Id { get; set; }
        public string ClientName { get; set; } = string.Empty;
        public string ServiceName { get; set; } = string.Empty;
        public string PackageName { get; set; } = string.Empty;
        public DateTime EventDate { get; set; }
        public DateTime StartTime { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    public class CommunicationParticipantViewModel
    {
        public string UserId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string Initials { get; set; } = string.Empty;
        public DateTime? LastLoginAt { get; set; }
    }

    public class CommunicationMessageViewModel
    {
        public int Id { get; set; }
        public string SenderId { get; set; } = string.Empty;
        public string SenderName { get; set; } = string.Empty;
        public string SenderRole { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DateTime SentAt { get; set; }
        public bool IsMine { get; set; }
        public bool IsReadByOthers { get; set; }
        public string MessageType { get; set; } = MessageTypes.Text;
        public string? RequestType { get; set; }
        public string? RequestStatus { get; set; }
        public string? ActionLink { get; set; }
    }

    public class CommunicationUserOptionViewModel
    {
        public string UserId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
    }
}
