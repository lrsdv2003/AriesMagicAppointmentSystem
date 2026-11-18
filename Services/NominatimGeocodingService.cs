using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AriesMagicAppointmentSystem.Services;

/// <summary>
/// Server-side search on explicit user submission only. The public Nominatim endpoint
/// prohibits autocomplete and limits all traffic to one request per second.
/// </summary>
public sealed class NominatimGeocodingService : IGeocodingService
{
    private readonly HttpClient _httpClient;
    private static readonly SemaphoreSlim RequestGate = new(1, 1);
    private static DateTimeOffset _lastRequestUtc = DateTimeOffset.MinValue;
    private static readonly ConcurrentDictionary<string, (DateTimeOffset Until, IReadOnlyList<GeocodingResult> Results)> Cache = new();

    public NominatimGeocodingService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri("https://nominatim.openstreetmap.org/");
        _httpClient.DefaultRequestHeaders.UserAgent.Clear();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("AriesMagicAppointmentSystem", "1.1"));
        _httpClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-PH,en;q=0.8");
    }

    public async Task<IReadOnlyList<GeocodingResult>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        var normalized = string.Join(" ", (query ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length is < 3 or > 180) return Array.Empty<GeocodingResult>();
        var cacheKey = normalized.ToLowerInvariant();
        if (Cache.TryGetValue(cacheKey, out var hit) && hit.Until > DateTimeOffset.UtcNow)
            return hit.Results;

        await RequestGate.WaitAsync(cancellationToken);
        try
        {
            if (Cache.TryGetValue(cacheKey, out hit) && hit.Until > DateTimeOffset.UtcNow)
                return hit.Results;
            var remaining = TimeSpan.FromMilliseconds(1100) - (DateTimeOffset.UtcNow - _lastRequestUtc);
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining, cancellationToken);
            _lastRequestUtc = DateTimeOffset.UtcNow;

            // Country filter prevents non-PH names without imposing a restrictive Manila viewbox.
            var url = "search?format=jsonv2&addressdetails=1&dedupe=1&limit=8&countrycodes=ph&q="
                      + Uri.EscapeDataString(normalized);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            using var response = await _httpClient.GetAsync(url, timeout.Token);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
            var list = new List<GeocodingResult>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (!item.TryGetProperty("lat", out var latitude) ||
                    !item.TryGetProperty("lon", out var longitude) ||
                    !double.TryParse(latitude.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) ||
                    !double.TryParse(longitude.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lon) ||
                    !VenueLocationPolicy.InPhilippines(lat, lon))
                    continue;
                var name = item.TryGetProperty("display_name", out var title)
                    ? title.GetString() ?? normalized : normalized;
                if (name.Length > 350) name = name[..350];
                var type = item.TryGetProperty("addresstype", out var kind)
                    ? kind.GetString() ?? "" : "";
                var coarse = type is "country" or "state" or "province" or "region" or "county"
                    or "city" or "town" or "municipality" or "village" or "suburb"
                    or "neighbourhood" or "district" or "postcode" or "administrative";
                var rank = item.TryGetProperty("place_rank", out var placeRank)
                           && placeRank.TryGetInt32(out var n) ? n : 0;
                var precise = !coarse && (rank >= 25 || type is "house" or "building" or "amenity"
                                          or "attraction" or "leisure" or "tourism");
                list.Add(new GeocodingResult(name,lat,lon,precise));
            }
            IReadOnlyList<GeocodingResult> results = list;
            if (Cache.Count > 250) Cache.Clear();
            Cache[cacheKey] = (DateTimeOffset.UtcNow.AddMinutes(list.Count > 0 ? 60 : 5), results);
            return results;
        }
        finally { RequestGate.Release(); }
    }
}
