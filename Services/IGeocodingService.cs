namespace AriesMagicAppointmentSystem.Services
{
    public interface IGeocodingService
    {
        Task<IReadOnlyList<GeocodingResult>> SearchAsync(string query, CancellationToken cancellationToken = default);
    }

    public record GeocodingResult(string DisplayName, double Latitude, double Longitude);
}
