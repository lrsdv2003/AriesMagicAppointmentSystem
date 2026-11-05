using System.Globalization;
using System.Security.Cryptography;
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
                await using (var proof = File.OpenRead(request.FilePath))
                    verification.ProofSha256 = Convert.ToHexString(await SHA256.HashDataAsync(proof, cancellationToken));

                var dataPath = Path.Combine(_environment.ContentRootPath, "tessdata");
                using var engine = new TesseractEngine(dataPath, "eng", EngineMode.Default);
                using var image = OcrImagePreprocessor.LoadForOcr(request.FilePath, out var lowResolution);
                using var page = engine.Process(image, PageSegMode.Auto);
                var text = (page.GetText() ?? string.Empty).Trim();
                var confidence = Math.Round((decimal)page.GetMeanConfidence() * 100m, 1);

                verification.RawText = text.Length > 12000 ? text[..12000] : text;
                verification.OcrConfidence = confidence;
                var amount = ReceiptAmountExtractor.Extract(text);
                verification.ExtractedAmount = amount.Amount;
                verification.AmountReliability = amount.Reliability;
                verification.ExtractedReferenceNumber = ExtractReference(text);
                verification.ExtractedSender = ExtractLabeledValue(text, "sender", "from", "account name");
                verification.ExtractedReceiver = ExtractLabeledValue(text, "receiver", "recipient", "merchant", "paid to", "to");
                verification.ExtractedStatus = ExtractStatus(text);
                verification.ExtractedPaymentMethod = ExtractPaymentMethod(text);
                verification.ExtractedDate = ExtractDate(text);
                verification.ExtractedTime = ExtractTime(text);

                var warnings = new List<string>();
                if (lowResolution)
                    warnings.Add("Low image resolution may reduce OCR accuracy.");
                if (amount.Ambiguous)
                    warnings.Add("Several plausible transaction amounts were found. OCR did not guess an amount.");
                else if (amount.Amount is null)
                    warnings.Add("Transaction amount was not reliably detected. Manual review required.");
                else if (amount.Reliability.StartsWith("Low", StringComparison.Ordinal))
                    warnings.Add("Amount has no explicit transfer label. Check it against the receipt.");
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

                if (!string.IsNullOrWhiteSpace(verification.ProofSha256))
                {
                    verification.IsDuplicateImage = await _context.OcrVerifications.AsNoTracking()
                        .AnyAsync(x => x.ProofSha256 == verification.ProofSha256 &&
                            (!request.PaymentId.HasValue || x.PaymentId != request.PaymentId), cancellationToken);
                    if (verification.IsDuplicateImage)
                        warnings.Add("The same receipt image was previously submitted. Owner must investigate before crediting payment.");
                }

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
                _logger.LogWarning("OCR processing failed. FailureClass={FailureClass}", ex.GetType().Name);
                verification.VerificationResult = OcrVerificationResults.OcrFailed;
                verification.ProcessingError = "OCR could not analyze this image. Manual review is required.";
                verification.WarningSummary = "OCR analysis failed. The screenshot must be reviewed manually.";
            }

            return verification;
        }

        private static string DetermineResult(OcrVerification v, OcrAnalysisRequest request, List<string> warnings)
        {
            if (string.IsNullOrWhiteSpace(v.RawText)) return OcrVerificationResults.OcrFailed;
            var mismatch = v.IsDuplicateReference || v.IsDuplicateImage || IsNegativeStatus(v.ExtractedStatus) ||
                (v.ExtractedAmount.HasValue && Math.Abs(v.ExtractedAmount.Value - request.ExpectedAmount) > 0.01m) ||
                (!string.IsNullOrWhiteSpace(v.ExtractedReceiver) && !OcrComparison.ReceiverMatches(v.ExtractedReceiver, request.ExpectedReceiver));
            if (mismatch) return OcrVerificationResults.MismatchDetected;

            var matches = 0;
            if (v.ExtractedAmount.HasValue && Math.Abs(v.ExtractedAmount.Value - request.ExpectedAmount) <= 0.01m) matches++;
            if (!string.IsNullOrWhiteSpace(v.ExtractedReceiver) && OcrComparison.ReceiverMatches(v.ExtractedReceiver, request.ExpectedReceiver)) matches++;
            if (IsPositiveStatus(v.ExtractedStatus)) matches++;
            if (!string.IsNullOrWhiteSpace(v.ExtractedPaymentMethod) && !string.IsNullOrWhiteSpace(request.ExpectedPaymentMethod) && ContainsNormalized(v.ExtractedPaymentMethod, request.ExpectedPaymentMethod)) matches++;
            if (!string.IsNullOrWhiteSpace(v.ExtractedReferenceNumber)) matches++;

            if (!v.ExtractedAmount.HasValue || string.IsNullOrWhiteSpace(v.ExtractedReferenceNumber) ||
                !IsPositiveStatus(v.ExtractedStatus) || (v.OcrConfidence ?? 0) < 60m ||
                v.AmountReliability is null || v.AmountReliability.StartsWith("Low", StringComparison.Ordinal))
                return OcrVerificationResults.ManualReviewRequired;
            if (matches >= 3 && warnings.Count == 0) return OcrVerificationResults.LikelyMatch;
            if (matches >= 1) return OcrVerificationResults.PartialMatch;
            return OcrVerificationResults.ManualReviewRequired;
        }

        private static string? ExtractReference(string text)
        {
            var labeled = Regex.Match(text, @"(?im)\b(?:reference(?:\s*(?:number|no\.?|id))?|ref\.?(?:\s*(?:number|no\.?|id))?|transaction\s*(?:id|no\.?|number)|trace\s*(?:id|no\.?|number))\s*[:#\-]?\s*([A-Z0-9][A-Z0-9\-]{7,34})\b");
            if (labeled.Success) return labeled.Groups[1].Value.Trim();
            // Do not guess from an unlabeled number: account, phone and booking IDs often look like references.
            return null;
        }

        private static string? ExtractLabeledValue(string text, params string[] labels)
        {
            foreach (var label in labels)
            {
                var m = Regex.Match(text, $@"(?im)^\s*{Regex.Escape(label)}\b\s*[:\-]?\s*([^\r\n]{{2,80}})$");
                if (m.Success) return m.Groups[1].Value.Trim();
            }
            return null;
        }

        private static string? ExtractStatus(string text)
        {
            if (Regex.IsMatch(text, @"\b(unsuccessful|unpaid|not\s+(?:successful|completed|paid))\b", RegexOptions.IgnoreCase)) return "Failed";
            if (Regex.IsMatch(text, @"\b(processing|in\s+progress|awaiting\s+confirmation)\b", RegexOptions.IgnoreCase)) return "Pending";
            foreach (var status in new[] { "Failed", "Cancelled", "Canceled", "Pending", "Successful", "Completed", "Paid" })
                if (Regex.IsMatch(text, $@"\b{status}\b", RegexOptions.IgnoreCase)) return status;
            return null;
        }

        private static string? ExtractPaymentMethod(string text)
        {
            if (Regex.IsMatch(text, @"\bGCash\b", RegexOptions.IgnoreCase)) return "GCash";
            if (Regex.IsMatch(text, @"\bMaya\b|PayMaya", RegexOptions.IgnoreCase)) return "Maya";
            if (Regex.IsMatch(text, @"bank\s*transfer|online\s*bank|\b(?:BDO|BPI|UnionBank|Metrobank|Landbank|RCBC|PNB|Security\s*Bank|China\s*Bank)\b", RegexOptions.IgnoreCase)) return "Bank Transfer";
            return null;
        }

        private static DateTime? ExtractDate(string text)
        {
            var patterns = new[] { @"\b\d{4}-\d{1,2}-\d{1,2}\b", @"\b\d{1,2}[/-]\d{1,2}[/-]\d{2,4}\b", @"\b(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Sept|Oct|Nov|Dec)[a-z]*\s+\d{1,2},?\s+\d{4}\b", @"\b\d{1,2}\s+(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Sept|Oct|Nov|Dec)[a-z]*\s+\d{4}\b" };
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
