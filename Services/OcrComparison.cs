using System.Text.RegularExpressions;

namespace AriesMagicAppointmentSystem.Services;

public static class OcrComparison
{
    // Presentation-only advice. No payment status, amount or approval rule is changed.
    public static string AmountLabel(decimal expected, decimal? detected, decimal? confidence)
    {
        if (!detected.HasValue) return "Not Detected";
        if (!confidence.HasValue || confidence.Value < 50m || detected.Value <= 0m) return "Uncertain OCR";
        var difference = detected.Value - expected;
        return Math.Abs(difference) <= .01m ? "Match" : difference > 0 ? "Possible overpayment" : "Possible underpayment";
    }

    public static bool IsSuccessful(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        Regex.IsMatch(value.Trim(), @"^(successful|success|completed|paid)$", RegexOptions.IgnoreCase);

    public static bool ReceiverMatches(string? actual, string? expected)
    {
        static string Normalize(string value) => Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9]", "");
        if (string.IsNullOrWhiteSpace(actual) || string.IsNullOrWhiteSpace(expected)) return false;
        var registered = Normalize(expected);
        return registered.Length > 0 && Normalize(actual).Contains(registered);
    }
}
