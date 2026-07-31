using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace ParkJomV2.Services;

public class NominatimService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<NominatimService> _logger;

    public NominatimService(HttpClient httpClient, ILogger<NominatimService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Search for places using the Nominatim Search API.
    /// </summary>
    public async Task<List<NominatimSearchResult>> SearchAsync(string query, int limit = 1)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<NominatimSearchResult>();

        var url = $"search?q={Uri.EscapeDataString(query)}&format=json&limit={limit}&addressdetails=1&countrycodes=my&accept-language=en";

        var response = await _httpClient.GetFromJsonAsync<List<NominatimSearchResult>>(url);

        _logger.LogInformation("Nominatim search for '{Query}' returned {Count} results", query, response?.Count ?? 0);

        return response ?? new List<NominatimSearchResult>();
    }

    /// <summary>
    /// Lookup a place by OSM type and ID using the Nominatim Lookup API.
    /// </summary>
    public async Task<NominatimSearchResult?> GetByOsmIdAsync(int osmId)
    {
        var url = $"lookup?osm_ids={osmId}&format=json&addressdetails=1&countrycodes=my&accept-language=en";

        var results = await _httpClient.GetFromJsonAsync<List<NominatimSearchResult>>(url);

        var result = results?.FirstOrDefault();
        if (result != null)
        {
            return result;
        }
        else
        {
            _logger.LogWarning("Nominatim lookup for {OsmId} returned no results", osmId);
        }

        return null;
    }
}

public class NominatimSearchResult
{
    [JsonPropertyName("osm_type")]
    public string OsmType { get; set; } = string.Empty;

    [JsonPropertyName("osm_id")]
    public long OsmId { get; set; }

    [JsonPropertyName("display_name")]
    public string Address { get; set; } = string.Empty;

    [JsonPropertyName("lat")]
    public string Lat { get; set; } = string.Empty;

    [JsonPropertyName("lon")]
    public string Lon { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("class")]
    public string? Class { get; set; }

    [JsonPropertyName("name")]
    public string PropertyName { get; set; } = string.Empty;
}
