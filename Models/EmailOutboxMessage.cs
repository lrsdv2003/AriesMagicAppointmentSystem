using System.ComponentModel.DataAnnotations;

namespace AriesMagicAppointmentSystem.Models;

// Private operational data. Never expose this table through a controller or export.
public sealed class EmailOutboxMessage
{
    public long Id { get; set; }
    [MaxLength(160)] public string EventKey { get; set; } = "";
    [MaxLength(320)] public string Recipient { get; set; } = "";
    [MaxLength(200)] public string Subject { get; set; } = "";
    public string HtmlBody { get; set; } = "";
    public byte[]? Attachment { get; set; }
    [MaxLength(200)] public string? AttachmentName { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime NextAttemptAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LockedUntilUtc { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }
    public DateTime? FailedAtUtc { get; set; }
    public int Attempts { get; set; }
    [MaxLength(100)] public string? LastFailureType { get; set; }
}
