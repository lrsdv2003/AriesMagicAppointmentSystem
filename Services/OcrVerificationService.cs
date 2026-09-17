using System.Globalization;
using System.Text.RegularExpressions;
using AriesMagicAppointmentSystem.Data;
using AriesMagicAppointmentSystem.Models;
using Microsoft.EntityFrameworkCore;
using Tesseract;

namespace AriesMagicAppointmentSystem.Services
{
    public sealed class OcrAnalysisRequest
    {
        public int? PaymentId { get; init; }
        public int? RefundRequestId { get; init; }
        public string VerificationPurpose { get; init; } = OcrVerificationPurposes.PaymentProof;
        public required string FilePath { get; init; }
        public decimal ExpectedAmount { get; init; }
        public string? ExpectedPaymentMethod { get; init; }
        public string ExpectedReceiver { get; init; } = "Aries Magic";
        public DateTime SubmittedAt { get; init; }
    }

    public interface IOcrVerificationService
    {
        Task<OcrVerification> AnalyzeAsync(OcrAnalysisRequest request, CancellationToken cancellationToken = default);
    }

    public sealed class OcrVerificationService : IOcrVerificationService
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<OcrVerificationService> _logger;

        public OcrVerificationService(ApplicationDbContext context, IWebHostEnvironment environment, ILogger<OcrVerificationService> logger)
        {
            _context = context;
            _environment = environment;
            _logger = logger;
        }

        public async Task<OcrVerification> AnalyzeAsync(OcrAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            var verification = new OcrVerification
            {
                PaymentId = request.PaymentId,
                RefundRequestId = request.RefundRequestId,
                VerificationPurpose = request.VerificationPurpose,
                ProcessedAt = DateTime.UtcNow,
                VerificationResult = OcrVerificationResults.ManualReviewRequired
            };

            try
            {
                var dataPath = Path.Combine(_environment.ContentRootPath, "tessdata");
                using var engine = new TesseractEngine(dataPath, "eng", EngineMode.Default);
                using var image = Pix.LoadFromFile(request.FilePath);
                using var page = engine.Process(image, PageSegMode.Auto);
                var text = (page.GetText() ?? string.Empty).Trim();
                var confidence = Math.Round((decimal)page.GetMeanConfidence() * 100m, 1);

                verification.RawText = text.Length > 12000 ? text[..12000] : text;
                verification.OcrConfidence = confidence;
                verification.ExtractedAmount = ExtractAmount(text, request.ExpectedAmount);
                verification.ExtractedReferenceNumber = ExtractReference(text);
                verification.ExtractedSender = ExtractLabeledValue(text, "sender", "from", "account name");
                verification.ExtractedReceiver = ExtractLabeledValue(text, "receiver", "recipient", "merchant", "paid to", "to");
                verification.ExtractedStatus = ExtractStatus(text);
                verification.ExtractedPaymentMethod = ExtractPaymentMethod(text);
                verification.ExtractedDate = ExtractDate(text);
                verification.ExtractedTime = ExtractTime(text);

                var warnings = new List<string>();
                if (image.Width < 500 || image.Height < 500)
                    warnings.Add("Low image resolution may reduce OCR accuracy.");
                if (confidence < 65m)
                    warnings.Add("Low OCR confidence - manual review recommended.");
                if (string.IsNullOrWhiteSpace(text))
                    warnings.Add("No readable text could be extracted from the screenshot.");
                if (verification.ExtractedAmount.HasValue && Math.Abs(verification.ExtractedAmount.Value - request.ExpectedAmount) > 0.01m)
                    warnings.Add("Payment amount does not match the required amount.");
                if (!string.IsNullOrWhiteSpace(verification.ExtractedReceiver) && !OcrComparison.ReceiverMatches(verification.ExtractedReceiver, request.ExpectedReceiver))
                    warnings.Add("Detected receiver does not match the registered payment account.");
                if (string.IsNullOrWhiteSpace(verification.ExtractedReferenceNumber))
                    warnings.Add("Transaction reference number could not be detected. Manual verification required.");
                if (IsNegativeStatus(verification.ExtractedStatus))
                    warnings.Add("Detected transaction status is not successful or completed.");
                if (verification.ExtractedDate.HasValue && Math.Abs((verification.ExtractedDate.Value.Date - request.SubmittedAt.Date).TotalDays) > 7)
                    warnings.Add("Transaction date may not match the expected payment period.");
                if (!string.IsNullOrWhiteSpace(verification.ExtractedPaymentMethod) && !string.IsNullOrWhiteSpace(request.ExpectedPaymentMethod) &&
                    !ContainsNormalized(verification.ExtractedPaymentMethod, request.ExpectedPaymentMethod))
                    warnings.Add("Detected payment method does not match the selected payment method.");

                if (!string.IsNullOrWhiteSpace(verification.ExtractedReferenceNumber))
                {
                    var duplicate = await _context.OcrVerifications.AsNoTracking()
                        .Where(x => x.ExtractedReferenceNumber == verification.ExtractedReferenceNumber)
                        .Where(x => !request.PaymentId.HasValue || x.PaymentId != request.PaymentId)
                        .OrderByDescending(x => x.ProcessedAt)
                        .FirstOrDefaultAsync(cancellationToken);
                    if (duplicate != null)
                    {
                        verification.IsDuplicateReference = true;
                        verification.DuplicatePaymentId = duplicate.PaymentId;
                        warnings.Add("Duplicate transaction reference detected. Manual review required.");
                    }
                }

                verification.WarningSummary = string.Join("\n", warnings.Distinct());
                verification.VerificationResult = DetermineResult(verification, request, warnings);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OCR processing failed for payment {PaymentId} refund {RefundRequestId}", request.PaymentId, request.RefundRequestId);
                verification.VerificationResult = OcrVerificationResults.OcrFailed;
                verification.ProcessingError = "OCR could not analyze this image. Manual review is required.";
                verification.WarningSummary = "OCR analysis failed. The screenshot must be reviewed manually.";
            }

            return verification;
        }

        private static string DetermineResult(OcrVerification v, OcrAnalysisRequest request, List<string> warnings)
        {
            if (string.IsNullOrWhiteSpace(v.RawText)) return OcrVerificationResults.OcrFailed;
            var mismatch = v.IsDuplicateReference || IsNegativeStatus(v.ExtractedStatus) ||
                (v.ExtractedAmount.HasValue && Math.Abs(v.ExtractedAmount.Value - request.ExpectedAmount) > 0.01m) ||
                (!string.IsNullOrWhiteSpace(v.ExtractedReceiver) && !OcrComparison.ReceiverMatches(v.ExtractedReceiver, request.ExpectedReceiver));
            if (mismatch) return OcrVerificationResults.MismatchDetected;

            var matches = 0;
            if (v.ExtractedAmount.HasValue && Math.Abs(v.ExtractedAmount.Value - request.ExpectedAmount) <= 0.01m) matches++;
            if (!string.IsNullOrWhiteSpace(v.ExtractedReceiver) && OcrComparison.ReceiverMatches(v.ExtractedReceiver, request.ExpectedReceiver)) matches++;
            if (IsPositiveStatus(v.ExtractedStatus)) matches++;
            if (!string.IsNullOrWhiteSpace(v.ExtractedPaymentMethod) && !string.IsNullOrWhiteSpace(request.ExpectedPaymentMethod) && ContainsNormalized(v.ExtractedPaymentMethod, request.ExpectedPaymentMethod)) matches++;
            if (!string.IsNullOrWhiteSpace(v.ExtractedReferenceNumber)) matches++;

            if ((v.OcrConfidence ?? 0) < 50m) return OcrVerificationResults.ManualReviewRequired;
            if (matches >= 3 && warnings.Count <= 1) return OcrVerificationResults.LikelyMatch;
            if (matches >= 1) return OcrVerificationResults.PartialMatch;
            return OcrVerificationResults.ManualReviewRequired;
        }

        private static decimal? ExtractAmount(string text, decimal expected)
        {
            var values = Regex.Matches(text, @"(?i)(?:PHP|PHP\s*₱|₱|P\s*)\s*([0-9]{1,3}(?:[, ][0-9]{3})*(?:\.[0-9]{1,2})?|[0-9]+(?:\.[0-9]{1,2})?)")
                .Select(m => decimal.TryParse(m.Groups[1].Value.Replace(",", "").Replace(" ", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? (decimal?)v : null)
                .Where(v => v.HasValue).Select(v => v!.Value).Where(v => v > 0).Distinct().ToList();
            if (values.Count == 0)
            {
                values = Regex.Matches(text, @"(?im)(?:amount|total|paid)\s*[:\-]?\s*(?:PHP|₱|P)?\s*([0-9,]+(?:\.[0-9]{1,2})?)")
                    .Select(m => decimal.TryParse(m.Groups[1].Value.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? (decimal?)v : null)
                    .Where(v => v.HasValue).Select(v => v!.Value).Where(v => v > 0).Distinct().ToList();
            }
            return values.OrderBy(v => Math.Abs(v - expected)).FirstOrDefault() is var best && best > 0 ? best : null;
        }

        private static string? ExtractReference(string text)
        {
            var labeled = Regex.Match(text, @"(?im)(?:reference|ref\.?|transaction\s*(?:id|no\.?|number)|trace\s*(?:id|no\.?)?)\s*[:#\-]?\s*([A-Z0-9\-]{8,30})");
            if (labeled.Success) return labeled.Groups[1].Value.Trim();
            var generic = Regex.Matches(text, @"\b[A-Z0-9]{10,24}\b", RegexOptions.IgnoreCase)
                .Select(m => m.Value).FirstOrDefault(v => v.Any(char.IsDigit) && v.Count(char.IsDigit) >= 8);
            return generic;
        }

        private static string? ExtractLabeledValue(string text, params string[] labels)
        {
            foreach (var label in labels)
            {
                var m = Regex.Match(text, $@"(?im)^\s*{Regex.Escape(label)}\s*[:\-]?\s*([^\r\n]{{2,80}})$");
                if (m.Success) return m.Groups[1].Value.Trim();
            }
            return null;
        }

        private static string? ExtractStatus(string text)
        {
            if (Regex.IsMatch(text, @"\b(unsuccessful|unpaid|not\s+(?:successful|completed|paid))\b", RegexOptions.IgnoreCase)) return "Failed";
            foreach (var status in new[] { "Failed", "Cancelled", "Canceled", "Pending", "Successful", "Completed", "Paid" })
                if (Regex.IsMatch(text, $@"\b{status}\b", RegexOptions.IgnoreCase)) return status;
            return null;
        }

        private static string? ExtractPaymentMethod(string text)
        {
            if (Regex.IsMatch(text, @"\bGCash\b", RegexOptions.IgnoreCase)) return "GCash";
            if (Regex.IsMatch(text, @"\bMaya\b|PayMaya", RegexOptions.IgnoreCase)) return "Maya";
            if (Regex.IsMatch(text, @"bank\s*transfer|online\s*bank", RegexOptions.IgnoreCase)) return "Bank Transfer";
            return null;
        }

        private static DateTime? ExtractDate(string text)
        {
            var patterns = new[] { @"\b\d{1,2}[/-]\d{1,2}[/-]\d{2,4}\b", @"\b(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Sept|Oct|Nov|Dec)[a-z]*\s+\d{1,2},?\s+\d{4}\b", @"\b\d{1,2}\s+(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Sept|Oct|Nov|Dec)[a-z]*\s+\d{4}\b" };
            foreach (var pattern in patterns)
            foreach (Match m in Regex.Matches(text, pattern, RegexOptions.IgnoreCase))
                if (DateTime.TryParse(m.Value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date)) return date.Date;
            return null;
        }

        private static string? ExtractTime(string text)
        {
            var m = Regex.Match(text, @"\b(?:[01]?\d|2[0-3]):[0-5]\d(?::[0-5]\d)?\s*(?:AM|PM)?\b", RegexOptions.IgnoreCase);
            return m.Success ? m.Value.Trim() : null;
        }

        private static bool ContainsNormalized(string source, string expected)
        {
            static string N(string s) => Regex.Replace(s.ToLowerInvariant(), @"[^a-z0-9]", "");
            var a = N(source); var b = N(expected);
            return !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && (a.Contains(b) || b.Contains(a));
        }

        private static bool IsPositiveStatus(string? status) => OcrComparison.IsSuccessful(status);
        private static bool IsNegativeStatus(string? status) => status is not null && Regex.IsMatch(status, "pending|failed|cancelled|canceled", RegexOptions.IgnoreCase);
    }
}
