using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace MusterHubCommand.Api.Services;

public record GeocodeResult(double Latitude, double Longitude, string DisplayName);

// Turns a typed address into coordinates for the "Locate" button on the
// manual New Incident form -- Vision-fed incidents already arrive with
// real coordinates and never call this. Backed by OpenStreetMap's
// Nominatim, the same OSM ecosystem the map tiles and Itinero routing
// already depend on, rather than a new paid geocoder/API key. Nominatim's
// public endpoint is rate-limited and meant for occasional, human-triggered
// lookups like this one -- not for bulk geocoding -- so this must never be
// called from a loop or on every keystroke.
public class GeocodingService(HttpClient httpClient)
{
    public async Task<GeocodeResult?> GeocodeAsync(string query, CancellationToken ct = default)
    {
        var url = $"search?format=json&limit=1&q={Uri.EscapeDataString(query)}";
        var results = await httpClient.GetFromJsonAsync<List<NominatimResult>>(url, ct);
        var top = results?.FirstOrDefault();
        if (top is null) return null;

        return new GeocodeResult(
            double.Parse(top.Lat, CultureInfo.InvariantCulture),
            double.Parse(top.Lon, CultureInfo.InvariantCulture),
            top.DisplayName);
    }

    private record NominatimResult(
        [property: JsonPropertyName("lat")] string Lat,
        [property: JsonPropertyName("lon")] string Lon,
        [property: JsonPropertyName("display_name")] string DisplayName);
}
