using System.ComponentModel.DataAnnotations;
namespace AriesMagicAppointmentSystem.Models;

public class EventFeedback
{
    public int Id { get; set; }
    public int BookingId { get; set; }
    public Booking Booking { get; set; } = null!;
    [MaxLength(450)] public string ClientId { get; set; } = "";
    [Range(1,5)] public int OverallRating { get; set; }
    [Range(1,5)] public int PerformanceRating { get; set; }
    [Range(1,5)] public int BookingRating { get; set; }
    [MaxLength(1500)] public string Experience { get; set; } = "";
    [MaxLength(1500)] public string Suggestions { get; set; } = "";
    [MaxLength(60)] public string PublicDisplayName { get; set; } = "";
    public bool TextConsent { get; set; }
    public bool NameConsent { get; set; }
    public bool MediaConsent { get; set; }
    public DateTime? ConsentedAtUtc { get; set; }
    public DateTime? WithdrawnAtUtc { get; set; }
    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(20)] public string ModerationStatus { get; set; } = "Pending";
    public bool Featured { get; set; }
    [MaxLength(450)] public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public DateTime? UnpublishedAtUtc { get; set; }
    [Timestamp] public byte[] Version { get; set; } = [];
    public List<FeedbackMedia> Media { get; set; } = [];
    public List<FeedbackPublicationEvent> PublicationEvents { get; set; } = [];
}
public class FeedbackMedia
{
    public int Id { get; set; }
    public int FeedbackId { get; set; }
    public EventFeedback Feedback { get; set; } = null!;
    [MaxLength(80)] public string StorageKey { get; set; } = "";
    [MaxLength(120)] public string DisplayFilename { get; set; } = "";
    [MaxLength(30)] public string ContentType { get; set; } = "image/jpeg";
    [MaxLength(10)] public string Kind { get; set; } = "Image";
    public long Size { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double? DurationSeconds { get; set; }
    [MaxLength(20)] public string ProcessingStatus { get; set; } = "Quarantined";
    public bool PublicApproved { get; set; }
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ScannedAtUtc { get; set; }
    [Timestamp] public byte[] Version { get; set; } = [];
}
public class FeedbackPublicationEvent
{
    public int Id { get; set; }
    public int FeedbackId { get; set; }
    public EventFeedback Feedback { get; set; } = null!;
    [MaxLength(450)] public string ActorId { get; set; } = "";
    [MaxLength(30)] public string Action { get; set; } = "";
    [MaxLength(500)] public string Reason { get; set; } = "";
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
}
public class FeedbackInvitation
{
    public int BookingId { get; set; }
    public Booking Booking { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
