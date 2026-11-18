using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace AriesMagicAppointmentSystem.Services;

/// <summary>
/// One source of truth for the eight existing event categories. No schema changes:
/// non-applicable optional values are cleared before writing a Booking record.
/// </summary>
public sealed record BookingEventFieldRule(
    bool ShowMotif,
    bool ShowTheme,
    bool ShowCelebrant,
    bool ShowAge,
    bool RequireCelebrant,
    bool RequireAge);

public sealed record NormalizedEventDetails(
    string Motif,
    string? Theme,
    string? Celebrant,
    int? Age);

public static class BookingEventDetailsPolicy
{
    public static readonly IReadOnlyList<string> EventTypes = new[]
    {
        "Birthday",
        "Company Events",
        "Bridal Shower",
        "Gender Reveal",
        "School Events",
        "Halloween",
        "Christmas Parties",
        "Easter Events"
    };

    private static readonly IReadOnlyDictionary<string, BookingEventFieldRule> Rules =
        new Dictionary<string, BookingEventFieldRule>(StringComparer.Ordinal)
        {
            // Birthday entertainment may require the name of the person being celebrated;
            // age is useful but not necessary to submit a request.
            ["Birthday"] = new(true, true, true, true, true, false),
            ["Bridal Shower"] = new(true, true, true, false, true, false),
            ["Gender Reveal"] = new(true, true, false, false, false, false),
            ["Company Events"] = new(false, false, false, false, false, false),
            ["School Events"] = new(false, false, false, false, false, false),
            ["Halloween"] = new(true, true, false, false, false, false),
            ["Christmas Parties"] = new(true, true, false, false, false, false),
            ["Easter Events"] = new(true, true, false, false, false, false)
        };

    public static BookingEventFieldRule? For(string? eventType) =>
        eventType != null && Rules.TryGetValue(eventType, out var rule) ? rule : null;

    public static NormalizedEventDetails ValidateAndNormalize(
        string? eventType,
        string? motif,
        string? theme,
        string? celebrant,
        int? age,
        ModelStateDictionary modelState)
    {
        var rule = For(eventType);
        if (rule == null)
        {
            modelState.AddModelError("EventType", "Choose a supported event type.");
            return new NormalizedEventDetails(string.Empty, null, null, null);
        }

        static string? TrimOptional(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        if (!rule.ShowMotif)
        {
            motif = null;
            modelState.Remove("Motif");
        }

        if (!rule.ShowTheme)
        {
            theme = null;
            modelState.Remove("PartyTheme");
        }

        if (!rule.ShowCelebrant)
        {
            celebrant = null;
            modelState.Remove("CelebrantName");
        }

        if (!rule.ShowAge)
        {
            age = null;
            modelState.Remove("Age");
        }

        var cleanMotif = rule.ShowMotif ? TrimOptional(motif) ?? string.Empty : string.Empty;
        var cleanTheme = rule.ShowTheme ? TrimOptional(theme) : null;
        var cleanCelebrant = rule.ShowCelebrant ? TrimOptional(celebrant) : null;

        if (rule.RequireCelebrant && string.IsNullOrWhiteSpace(cleanCelebrant))
            modelState.AddModelError("CelebrantName",
                "Enter the celebrant name for this event.");

        if (rule.RequireAge && !age.HasValue)
            modelState.AddModelError("Age",
                "Enter the celebrant age for this event.");

        return new NormalizedEventDetails(cleanMotif, cleanTheme, cleanCelebrant, age);
    }
}
