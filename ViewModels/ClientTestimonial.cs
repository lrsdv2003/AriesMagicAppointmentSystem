namespace AriesMagicAppointmentSystem.ViewModels;

// Explicit public projection. Never add private survey answers or account identifiers here.
public sealed record ClientTestimonial(string Excerpt, string PublicName, string EventLabel,
    bool IsVerified, bool IsApproved, bool HasPublicationConsent)
{
    public bool CanPublish => IsVerified && IsApproved && HasPublicationConsent;
    public int? OverallRating { get; init; }
    public IReadOnlyList<int> PublicImageIds { get; init; } = [];
}
