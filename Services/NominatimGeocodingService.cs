using System.Net.Http.Headers;
using System.Text.Json;

namespace AriesMagicAppointmentSystem.Services
{
    public class NominatimGeocodingService : IGeocodingService
    {
        private readonly HttpClient _httpClient;

        public NominatimGeocodingService(HttpClient httpClient)
        {
            _httpClient = httpClient;
            _httpClient.BaseAddress = new Uri("https://nominatim.openstreetmap.org/");
            _httpClient.DefaultRequestHeaders.UserAgent.Clear();
            _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AriesMagicAppointmentSystem", "1.0"));
        }

        public async Task<IReadOnlyList<GeocodingResult>> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
                return Array.Empty<GeocodingResult>();

            var url = $"search?format=jsonv2&limit=5&countrycodes=ph&q={Uri.EscapeDataString(query)}";
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            var results = new List<GeocodingResult>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (!item.TryGetProperty("lat", out var latElement) || !item.TryGetProperty("lon", out var lonElement))
                    continue;

                if (!double.TryParse(latElement.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lat) ||
                    !double.TryParse(lonElement.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lon))
                    continue;

                var displayName = item.TryGetProperty("display_name", out var nameElement)
                    ? nameElement.GetString() ?? query
                    : query;

                results.Add(new GeocodingResult(displayName, lat, lon));
            }

            return results;
        }
    }
}
