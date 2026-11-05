using System.Globalization;
using System.Text.RegularExpressions;

namespace AriesMagicAppointmentSystem.Services;

/// <summary>
/// Receipt-only analysis. The customer's selected/expected amount is never used to pick a value.
/// This does not establish that the payment actually cleared.
/// </summary>
public static class ReceiptAmountExtractor
{
    public sealed record Result(decimal? Amount, string Reliability, bool Ambiguous, string? Evidence);
    private sealed record Candidate(decimal Value, int Priority, string Label);

    private static readonly Regex Number = new(
        @"(?<![\w\d])(?:(?:PHP|₱)\s*)?(?<value>\d{1,3}(?:[, ]\d{3})+(?:\.\d{1,2})?|\d+(?:\.\d{1,2})?)(?:\s*PHP)?(?![\w\d])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(150));
    private static readonly Regex Currency = new(
        @"(?:₱|\bPHP\b)\s*(?<value>\d{1,3}(?:[, ]\d{3})+(?:\.\d{1,2})?|\d+(?:\.\d{1,2})?)|(?<value>\d{1,3}(?:[, ]\d{3})+(?:\.\d{1,2})?|\d+(?:\.\d{1,2})?)\s*\bPHP\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(150));
    private static readonly Regex StrongLabel = new(
        @"\b(amount\s*(?:sent|paid|transferred|received)|(?:transfer|transferred|sent|paid|payment|transaction)\s*amount|total\s*(?:paid|sent|transferred)|you\s*sent|you\s*paid)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(150));
    private static readonly Regex GeneralLabel = new(
        @"\b(amount|total|payment)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(150));
    private static readonly Regex Excluded = new(
        @"\b(balance|available|remaining|opening|closing|fee|charge|cashback|reward|tax|discount|account\s*(?:number|no)|reference|ref\s*(?:no|number)|previous|before|change|limit)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(150));
    private static readonly Regex TransactionContext = new(
        @"\b(transaction|transfer|sent|payment|paid|receipt|gcash|maya|bank|reference|successful|completed)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(150));

    public static Result Extract(string? ocrText)
    {
        if (string.IsNullOrWhiteSpace(ocrText)) return new(null, "Unavailable", false, null);
        var lines = ocrText.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var receiptContext = TransactionContext.IsMatch(ocrText);
        var candidates = new List<Candidate>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length > 200 || Excluded.IsMatch(line)) continue;
            var strong = StrongLabel.IsMatch(line);
            var general = !strong && GeneralLabel.IsMatch(line);
            var priority = strong ? 3 : general ? 2 : receiptContext ? 1 : 0;
            if (priority == 0) continue;

            // Prefer amounts attached to a transaction label, rather than other figures
            // elsewhere on the screenshot (balances, phone numbers, fee and reference IDs).
            var label = strong ? "Transaction amount label" : general ? "General amount label" : "Unlabeled currency";
            var matched = false;
            foreach (Match m in (strong || general ? Number : Currency).Matches(line))
            {
                if (!m.Success) continue;
                if (TryAmount(m.Groups["value"].Value, out var value))
                {
                    // Unlabeled amounts require a currency symbol, even if the receipt looks genuine.
                    if (priority == 1 && !Currency.IsMatch(m.Value)) continue;
                    candidates.Add(new(value, priority, label));
                    matched = true;
                }
            }
            // Many payment apps print "Amount Sent" and the amount on separate lines.
            if (!matched && (strong || general) && i + 1 < lines.Length)
            {
                var next = lines[i + 1].Trim();
                if (next.Length <= 35 && !Excluded.IsMatch(next))
                {
                    foreach (Match m in Number.Matches(next))
                    {
                        if (TryAmount(m.Groups["value"].Value, out var value))
                            candidates.Add(new(value, priority, label + " (next line)"));
                    }
                }
            }
        }
        if (candidates.Count == 0) return new(null, "Unavailable", false, null);
        var priorityMax = candidates.Max(c => c.Priority);
        var top = candidates.Where(c => c.Priority == priorityMax).ToList();
        var unique = top.Select(c => c.Value).Distinct().ToList();
        if (unique.Count != 1)
            return new(null, "Ambiguous", true, "Conflicting transaction-amount candidates");
        var winner = top.First(c => c.Value == unique[0]);
        return new(winner.Value, priorityMax == 3 ? "High (label-backed)" : priorityMax == 2 ? "Medium (label-backed)" : "Low (unlabeled)",
            false, winner.Label);
    }

    private static bool TryAmount(string raw, out decimal amount)
    {
        amount = 0m;
        var normalized = raw.Replace(",", "").Replace(" ", "");
        if (!decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount))
            return false;
        return amount > 0m && amount <= 1_000_000m && decimal.Round(amount, 2) == amount;
    }
}
