using System.Text.RegularExpressions;

namespace AriesMagicAppointmentSystem.Services;

public static class OcrComparison
{
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
