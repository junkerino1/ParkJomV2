namespace ParkJomV2.Services;
using System.Net.Http.Json;
using System.Text.Json;
public class OsrmService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OsrmService> _logger;

    public OsrmService(HttpClient httpClient, ILogger<OsrmService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Calculate walking distances/times from a single origin to multiple destinations
    /// using OSRM table endpoint with foot profile.
    /// Returns (distance in km, time in minutes) for each destination.
    /// </summary>
    public async Task<List<(double? DistanceKm, double? TimeMinutes)>> GetWalkingDistancesAsync(
        double originLat, double originLon,
        List<(double Lat, double Lon)> destinations)
    {
        if (destinations.Count == 0)
            return new List<(double?, double?)>();

        // Build coordinates string: lon,lat;lon,lat;...
        var coords = $"{originLon},{originLat}";
        foreach (var (lat, lon) in destinations)
        {
            coords += $";{lon},{lat}";
        }

        var destIndices = string.Join(";", Enumerable.Range(1, destinations.Count));
        var url = $"table/v1/foot/{coords}?sources=0&destinations={destIndices}&annotations=distance,duration";
        
        // valid url
        // var url = $"/table/v1/foot/101.712779,3.202784;101.722625,3.217872;101.712779,3.202784?sources=0&destinations=1;2&annotations=distance,duration";

        OsrmTableResponse? response;
        try
        {
            using var httpResponse = await _httpClient.GetAsync(url);
            if (!httpResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("OSRM table request failed. Url={Url}, StatusCode={StatusCode}", url, (int)httpResponse.StatusCode);
                return destinations.Select(_ => ((double?)null, (double?)null)).ToList();
            }

            response = await httpResponse.Content.ReadFromJsonAsync<OsrmTableResponse>();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "OSRM table request error. Url={Url}", url);
            return destinations.Select(_ => ((double?)null, (double?)null)).ToList();
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "OSRM table request timed out. Url={Url}", url);
            return destinations.Select(_ => ((double?)null, (double?)null)).ToList();
        }

        // log OSRM response for debugging
        _logger.LogInformation("OSRM response: {Response}",
            JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                WriteIndented = true
            }));

        if (response?.Code != "Ok" || response.Distances == null || response.Durations == null
            || response.Distances.Count == 0 || response.Durations.Count == 0)
        {
            _logger.LogWarning("OSRM returned error or empty response: Code={Code}", response?.Code);
            return destinations.Select(_ => ((double?)null, (double?)null)).ToList();
        }

        var distances = response.Distances[0];
        var durations = response.Durations[0];

        var results = new List<(double? DistanceKm, double? TimeMinutes)>();
        for (int i = 0; i < distances.Count && i < durations.Count; i++)
        {
            results.Add((
                DistanceKm: distances[i] / 1000.0,
                TimeMinutes: durations[i] / 60.0
            ));
        }

        for (int i = 0; i < results.Count; i++)
        {
            _logger.LogInformation(
                "Result {Index}: DistanceKm={Distance}, TimeMinutes={Time}",
                i,
                results[i].DistanceKm,
                results[i].TimeMinutes);
        }
        return results;
    }

    /// <summary>
    /// Calculate walking distance and time between a single origin-destination pair
    /// using OSRM route endpoint with foot profile.
    /// Returns (distance in km, time in minutes).
    /// </summary>
    public async Task<(double? DistanceKm, double? TimeMinutes)> GetWalkingDistanceAsync(
        double originLat, double originLon,
        double destLat, double destLon)
    {
        var url = $"route/v1/foot/{originLon},{originLat};{destLon},{destLat}?overview=false";

        // _logger.LogInformation("OSRM Route Request URL: {Url}", url);

        OsrmRouteResponse? response;
        try
        {
            using var httpResponse = await _httpClient.GetAsync(url);
            if (!httpResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("OSRM route request failed. Url={Url}, StatusCode={StatusCode}", url, (int)httpResponse.StatusCode);
                return (null, null);
            }

            response = await httpResponse.Content.ReadFromJsonAsync<OsrmRouteResponse>();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "OSRM route request error. Url={Url}", url);
            return (null, null);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "OSRM route request timed out. Url={Url}", url);
            return (null, null);
        }

        if (response?.Code != "Ok" || response.Routes == null || response.Routes.Count == 0)
            return (null, null);

        var route = response.Routes[0];
        return (route.Distance / 1000.0, route.Duration / 60.0);
    }
}

public class OsrmTableResponse
{
    public string Code { get; set; } = string.Empty;
    public List<List<double>>? Durations { get; set; }
    public List<List<double>>? Distances { get; set; }
    public List<OsrmWaypoint>? Sources { get; set; }
    public List<OsrmWaypoint>? Destinations { get; set; }
}

public class OsrmRouteResponse
{
    public string Code { get; set; } = string.Empty;
    public List<OsrmRoute>? Routes { get; set; }
}

public class OsrmRoute
{
    public double Distance { get; set; }
    public double Duration { get; set; }
}

public class OsrmWaypoint
{
    public double? Distance { get; set; }
    public string? Name { get; set; }
    public List<double>? Location { get; set; }
}
