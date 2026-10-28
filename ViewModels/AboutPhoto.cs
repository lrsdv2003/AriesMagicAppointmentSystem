namespace AriesMagicAppointmentSystem.ViewModels;

// Presentation metadata for the owner's approved photographs; no package association.
public sealed record AboutPhoto(string Key, string Original, string Alt, int Width, int Height,
    string Sizes, bool Priority = false, string? Caption = null, string? Credit = null);
