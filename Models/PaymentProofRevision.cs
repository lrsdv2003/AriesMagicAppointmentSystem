using System.ComponentModel.DataAnnotations;

namespace AriesMagicAppointmentSystem.Models;

public sealed class PaymentProofRevision
{
    public int Id { get; set; }
    public int PaymentId { get; set; }
    public Payment Payment { get; set; } = null!;
    [MaxLength(500)] public string ProofImagePath { get; set; } = "";
    public decimal Amount { get; set; }
    [MaxLength(40)] public string PaymentMethod { get; set; } = "";
    [MaxLength(30)] public string Status { get; set; } = PaymentStatus.Pending;
    public DateTime UploadedAt { get; set; }
    [MaxLength(1000)] public string? ReviewerNote { get; set; }
    public DateTime ArchivedAtUtc { get; set; } = DateTime.UtcNow;
}
