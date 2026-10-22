namespace AriesMagicAppointmentSystem.Services;

public static class ImageUploadValidation
{
    public static async Task<bool> HasMatchingSignatureAsync(IFormFile file)
    {
        var header = new byte[12];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false);
        if (read < 12) return false;
        return Path.GetExtension(file.FileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => file.ContentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase)
                && header[0] == 255 && header[1] == 216 && header[2] == 255,
            ".png" => file.ContentType.Equals("image/png", StringComparison.OrdinalIgnoreCase)
                && header.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            ".webp" => file.ContentType.Equals("image/webp", StringComparison.OrdinalIgnoreCase)
                && header.AsSpan(0, 4).SequenceEqual("RIFF"u8) && header.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            _ => false
        };
    }
}
