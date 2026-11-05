using AriesMagicAppointmentSystem.Services;

static class OcrAmountRegressionTests
{
    public static void Run()
    {
        var checks=0;
        void Check(bool ok,string label)
        {
            if (!ok) throw new Exception("FAILED: "+label);
            Console.WriteLine("PASS: "+label);
            checks++;
        }

        var exact=ReceiptAmountExtractor.Extract("GCash Transfer Successful\nAmount Sent: PHP 2,000.00\nReference Number: 1234567890123");
        Check(exact.Amount==2000m,"Expected PHP 2,000 independently extracted when receipt shows PHP 2,000");
        Check(exact.Reliability.StartsWith("High"),"Explicit transaction label yields meaningful reliability");

        var greater=ReceiptAmountExtractor.Extract("GCash Transfer Successful\nAmount Sent: PHP 3,500.00\nWallet Balance ₱2,000.00\nReference Number 1234567890123");
        Check(greater.Amount==3500m,"Receipt PHP 3,500 preferred over unrelated PHP 2,000 wallet balance");
        Check(OcrComparison.AmountLabel(2000m,greater.Amount,90m)=="Possible overpayment","PHP 3,500 vs expected PHP 2,000 flagged, not replaced");
        var smaller=ReceiptAmountExtractor.Extract("Maya\nTransfer Amount: ₱1,500.00\nReference Number 1234567890123");
        Check(smaller.Amount==1500m&&OcrComparison.AmountLabel(2000m,smaller.Amount,90m)=="Possible underpayment","Receipt below selected amount flagged");

        var fees=ReceiptAmountExtractor.Extract("Bank Transfer Successful\nTotal Paid: PHP 3,500.00\nService Fee: ₱15.00\nAvailable Balance ₱9,000.00");
        Check(fees.Amount==3500m,"Balance and fees ignored even if their values are larger");
        var ambiguous=ReceiptAmountExtractor.Extract("GCash successful\nAmount Sent PHP 3500.00\nTransfer Amount PHP 2000.00");
        Check(ambiguous.Amount is null && ambiguous.Ambiguous,"Equal-priority conflicting amounts refuse to guess");
        var unreadable=ReceiptAmountExtractor.Extract("unrelated holiday photo");
        Check(unreadable.Amount is null && unreadable.Reliability=="Unavailable","Unrelated text never fabricates an amount");
        var balanceOnly=ReceiptAmountExtractor.Extract("GCash\nAvailable balance ₱8,000.00\nService fee ₱15.00");
        Check(balanceOnly.Amount is null,"Balance and fee values cannot masquerade as a transfer");
        var currencyOnly=ReceiptAmountExtractor.Extract("₱4,000.00");
        Check(currencyOnly.Amount is null,"Currency-only image without transaction context requires review");
        var singleUnlabeled=ReceiptAmountExtractor.Extract("Transaction completed\n₱3,500.00");
        Check(singleUnlabeled.Amount==3500m&&singleUnlabeled.Reliability.StartsWith("Low"),"Single unlabeled amount is displayed with low reliability");
        var mixedUnlabeled=ReceiptAmountExtractor.Extract("Transaction completed\n₱2,000.00\n₱3,500.00");
        Check(mixedUnlabeled.Amount is null&&mixedUnlabeled.Ambiguous,"Multiple unlabeled amounts require manual review");
        // Exercise the actual OCR normalization path without customer data or external services.
        var temp = Path.Combine(Path.GetTempPath(), "aries-ocr-preprocess-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            using var bitmap = new SkiaSharp.SKBitmap(360, 180);
            using (var canvas = new SkiaSharp.SKCanvas(bitmap))
            {
                canvas.Clear(SkiaSharp.SKColors.White);
                using var ink = new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.Black, IsAntialias = true };
                using var font = new SkiaSharp.SKFont(SkiaSharp.SKTypeface.Default, 22);
                canvas.DrawText("Amount Sent PHP 3500", 12, 75, SkiaSharp.SKTextAlign.Left, font, ink);
            }
            using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
            foreach (var format in new[] { (SkiaSharp.SKEncodedImageFormat.Png, ".png"), (SkiaSharp.SKEncodedImageFormat.Webp, ".webp") })
            {
                var target = Path.Combine(temp, "receipt" + format.Item2);
                using (var data = image.Encode(format.Item1, 95))
                    File.WriteAllBytes(target, data.ToArray());
                using var pix = OcrImagePreprocessor.LoadForOcr(target, out var lowResolution);
                Check(pix.Width >= 360 && pix.Height >= 180 && lowResolution,
                    "OCR preprocessing decodes and safely upscales " + format.Item2);
            }
        }
        finally { Directory.Delete(temp, true); }
        Console.WriteLine("SUCCESS: "+checks+" independent OCR amount/image checks passed.");
    }
}
