namespace AriesMagicAppointmentSystem.Services;

public static class ImageUploadValidation
{
    private static readonly SemaphoreSlim DecodeGate = new(1, 1);

    public static async Task<bool> HasMatchingSignatureAsync(IFormFile file)
    {
        if (file.Length < 12 || file.Length > 10 * 1024 * 1024) return false;
        var header = new byte[12];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false);
        if (read < 12) return false;
        var matches = Path.GetExtension(file.FileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => string.Equals(file.ContentType,"image/jpeg", StringComparison.OrdinalIgnoreCase)
                && header[0] == 255 && header[1] == 216 && header[2] == 255,
            ".png" => string.Equals(file.ContentType,"image/png", StringComparison.OrdinalIgnoreCase)
                && header.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            ".webp" => string.Equals(file.ContentType,"image/webp", StringComparison.OrdinalIgnoreCase)
                && header.AsSpan(0, 4).SequenceEqual("RIFF"u8) && header.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            _ => false
        };
        if (!matches || !await DecodeGate.WaitAsync(TimeSpan.FromSeconds(5))) return false;
        try
        {
            await using var input = file.OpenReadStream();
            using var bytes = new MemoryStream();
            var buffer = new byte[65536]; int count;
            while ((count = await input.ReadAsync(buffer)) > 0)
            {
                if (bytes.Length + count > 10 * 1024 * 1024) return false;
                await bytes.WriteAsync(buffer.AsMemory(0, count));
            }
            if (bytes.Length != file.Length) return false;
            using var data = SkiaSharp.SKData.CreateCopy(bytes.ToArray());
            using var codec = SkiaSharp.SKCodec.Create(data);
            var expected = Path.GetExtension(file.FileName).ToLowerInvariant() switch
            {
                ".png" => SkiaSharp.SKEncodedImageFormat.Png,
                ".webp" => SkiaSharp.SKEncodedImageFormat.Webp,
                _ => SkiaSharp.SKEncodedImageFormat.Jpeg
            };
            if (codec is null || codec.EncodedFormat != expected || codec.FrameCount > 1
                || codec.Info.Width < 1 || codec.Info.Height < 1 || codec.Info.Width > 6000 || codec.Info.Height > 6000
                || (long)codec.Info.Width * codec.Info.Height > 16_000_000) return false;
            using var bitmap = new SkiaSharp.SKBitmap(new SkiaSharp.SKImageInfo(codec.Info.Width, codec.Info.Height,
                SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Premul));
            return codec.GetPixels(bitmap.Info, bitmap.GetPixels()) == SkiaSharp.SKCodecResult.Success;
        }
        catch (Exception) { return false; }
        finally { DecodeGate.Release(); }
    }
}
