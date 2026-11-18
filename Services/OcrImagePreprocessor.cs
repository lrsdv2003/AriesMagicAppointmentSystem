using SkiaSharp;
using Tesseract;

namespace AriesMagicAppointmentSystem.Services;

/// <summary>Decode recognized image formats, honor common EXIF rotations, and normalize for OCR.</summary>
public static class OcrImagePreprocessor
{
    public static Pix LoadForOcr(string filePath, out bool lowResolution)
    {
        using var input = File.OpenRead(filePath);
        using var codec = SKCodec.Create(input) ?? throw new InvalidDataException("Unsupported image.");
        using var original = SKBitmap.Decode(filePath) ?? throw new InvalidDataException("Invalid image.");
        if (original.Width < 1 || original.Height < 1 || original.Width > 6000 || original.Height > 6000 ||
            (long)original.Width * original.Height > 16_000_000)
            throw new InvalidDataException("Image dimensions are outside the supported proof limits.");

        lowResolution = original.Width < 500 || original.Height < 500;
        var origin = codec.EncodedOrigin;
        var rotated = origin is SKEncodedOrigin.RightTop or SKEncodedOrigin.LeftBottom or SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightBottom;
        var orientedWidth = rotated ? original.Height : original.Width;
        var orientedHeight = rotated ? original.Width : original.Height;
        var scale = Math.Clamp(1200.0 / Math.Max(orientedWidth, orientedHeight), 1.0, 2.0);
        var width = (int)Math.Round(orientedWidth * scale);
        var height = (int)Math.Round(orientedHeight * scale);

        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            canvas.Scale((float)scale, (float)scale);
            switch (origin)
            {
                case SKEncodedOrigin.BottomRight:
                    canvas.Translate(original.Width, original.Height);
                    canvas.RotateDegrees(180);
                    break;
                case SKEncodedOrigin.RightTop:
                    canvas.Translate(original.Height, 0);
                    canvas.RotateDegrees(90);
                    break;
                case SKEncodedOrigin.LeftBottom:
                    canvas.Translate(0, original.Width);
                    canvas.RotateDegrees(270);
                    break;
                case SKEncodedOrigin.LeftTop:
                    canvas.RotateDegrees(90);
                    canvas.Scale(1, -1);
                    break;
                case SKEncodedOrigin.RightBottom:
                    canvas.Translate(original.Height, original.Width);
                    canvas.RotateDegrees(90);
                    canvas.Scale(-1, 1);
                    break;
                case SKEncodedOrigin.TopRight:
                    canvas.Translate(original.Width, 0);
                    canvas.Scale(-1, 1);
                    break;
                case SKEncodedOrigin.BottomLeft:
                    canvas.Translate(0, original.Height);
                    canvas.Scale(1, -1);
                    break;
                default: break;
            }
            using var paint = new SKPaint { IsAntialias = true };
            canvas.DrawBitmap(original, 0, 0, new SKSamplingOptions(SKFilterMode.Linear), paint);
            canvas.Flush();
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var bytes = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidDataException("Image could not be prepared for OCR.");
        return Pix.LoadFromMemory(bytes.ToArray());
    }
}
