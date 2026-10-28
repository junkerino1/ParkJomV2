using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using ParkJomV2.DTOs;
using TransitRealtime;

namespace ParkJomV2.Services;

public class GtfsRealtimeService
{
    private const string BaseApiUrl = "https://api.data.gov.my/gtfs-realtime/vehicle-position";
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly GtfsService _gtfsService;
    private readonly ILogger<GtfsRealtimeService> _logger;

    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(25);

    private static readonly List<GtfsRealtimeAgencyInfo> SupportedAgencies = new()
    {
        new()
        {
            AgencyKey = "ktmb",
            AgencyName = "Keretapi Tanah Melayu Berhad (KTMB)",
            Mode = "Train",
            Categories = new List<string> { "komuter", "ets" },
            Description = "Live tracking of KTM Komuter and ETS electric trains across the Klang Valley and Peninsular Malaysia."
        },
        new()
        {
            AgencyKey = "prasarana",
            AgencyName = "Prasarana Malaysia (Rapid KL)",
            Mode = "Bus",
            Categories = new List<string> { "rapid-bus-mrtfeeder", "rapid-bus-kl", "rapid-bus-penang", "rapid-bus-kuantan" },
            Description = "Live vehicle positions for Rapid Bus KL city buses and MRT Feeder buses connecting to rail transit."
        },
        new()
        {
            AgencyKey = "mybas-johor",
            AgencyName = "myBAS Johor Bahru",
            Mode = "Bus",
            Categories = new List<string>(),
            Description = "Stage Bus Service Transformation (SBST) live positions for Johor Bahru."
        }
    };

    public GtfsRealtimeService(
        HttpClient httpClient,
        IMemoryCache cache,
        GtfsService gtfsService,
        ILogger<GtfsRealtimeService> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _gtfsService = gtfsService;
        _logger = logger;
    }

    public List<GtfsRealtimeAgencyInfo> GetSupportedAgencies() => SupportedAgencies;

    /// <summary>
    /// Fetches live vehicle positions from Malaysia's data.gov.my GTFS-Realtime API.
    /// Results are cached for 25 seconds to respect upstream 30s refresh intervals.
    /// </summary>
    public async Task<GtfsRealtimeFeedResponseDto> GetVehiclePositionsAsync(
        string agency = "prasarana",
        string? category = "rapid-bus-mrtfeeder",
        CancellationToken cancellationToken = default)
    {
        var normalizedAgency = agency.Trim().ToLowerInvariant();
        var normalizedCategory = string.IsNullOrWhiteSpace(category) ? null : category.Trim().ToLowerInvariant();

        // If prasarana is requested without category, default to rapid-bus-mrtfeeder
        if (normalizedAgency == "prasarana" && string.IsNullOrEmpty(normalizedCategory))
        {
            normalizedCategory = "rapid-bus-mrtfeeder";
        }

        var cacheKey = $"gtfs_rt_{normalizedAgency}_{normalizedCategory ?? "default"}";

        if (_cache.TryGetValue(cacheKey, out GtfsRealtimeFeedResponseDto? cachedResponse) && cachedResponse != null)
        {
            return cachedResponse;
        }

        var response = await FetchAndParseFeedAsync(normalizedAgency, normalizedCategory, cancellationToken);
        _cache.Set(cacheKey, response, CacheDuration);
        return response;
    }

    /// <summary>
    /// Returns live vehicle positions as GeoJSON FeatureCollection for direct Leaflet map consumption.
    /// </summary>
    public async Task<GtfsRealtimeGeoJsonCollection> GetVehiclesGeoJsonAsync(
        string agency = "prasarana",
        string? category = "rapid-bus-mrtfeeder",
        CancellationToken cancellationToken = default)
    {
        var feed = await GetVehiclePositionsAsync(agency, category, cancellationToken);

        var geoJson = new GtfsRealtimeGeoJsonCollection
        {
            Properties = new Dictionary<string, object>
            {
                ["agency"] = feed.Agency,
                ["category"] = feed.Category ?? "",
                ["totalVehicles"] = feed.TotalVehicles,
                ["updatedAt"] = feed.UpdatedAt.ToString("o")
            }
        };

        foreach (var v in feed.Vehicles)
        {
            geoJson.Features.Add(new GtfsRealtimeGeoJsonFeature
            {
                Properties = v,
                Geometry = new GtfsRealtimeGeometry
                {
                    Coordinates = new List<double> { v.Longitude, v.Latitude }
                }
            });
        }

        return geoJson;
    }

    /// <summary>
    /// Finds live transit vehicles near specific GPS coordinates (user location or parking spot).
    /// </summary>
    public async Task<List<GtfsRealtimeVehicleDto>> GetNearbyVehiclesAsync(
        double lat,
        double lng,
        double radiusMeters = 3000,
        string agency = "prasarana",
        string? category = "rapid-bus-mrtfeeder",
        CancellationToken cancellationToken = default)
    {
        var feed = await GetVehiclePositionsAsync(agency, category, cancellationToken);

        return feed.Vehicles
            .Select(v =>
            {
                var dist = GtfsService.CalculateDistanceMeters(lat, lng, v.Latitude, v.Longitude);
                return new GtfsRealtimeVehicleDto
                {
                    Id = v.Id,
                    VehicleId = v.VehicleId,
                    LicensePlate = v.LicensePlate,
                    Label = v.Label,
                    TripId = v.TripId,
                    RouteId = v.RouteId,
                    Agency = v.Agency,
                    Category = v.Category,
                    Latitude = v.Latitude,
                    Longitude = v.Longitude,
                    Bearing = v.Bearing,
                    SpeedKmh = v.SpeedKmh,
                    Odometer = v.Odometer,
                    CurrentStatus = v.CurrentStatus,
                    StopId = v.StopId,
                    CurrentStopSequence = v.CurrentStopSequence,
                    CongestionLevel = v.CongestionLevel,
                    Timestamp = v.Timestamp,
                    DistanceMeters = Math.Round(dist, 1),
                    EstimatedArrivalMinutes = v.SpeedKmh.HasValue && v.SpeedKmh.Value > 5
                        ? (int)Math.Max(1, Math.Round((dist / 1000.0) / v.SpeedKmh.Value * 60.0))
                        : (int)Math.Ceiling(dist / 400.0) // fallback ~25 km/h transit speed
                };
            })
            .Where(v => v.DistanceMeters <= radiusMeters)
            .OrderBy(v => v.DistanceMeters)
            .ToList();
    }

    /// <summary>
    /// Finds live MRT Feeder buses and transit arriving near a specific rail station.
    /// </summary>
    public async Task<List<GtfsRealtimeVehicleDto>> GetStationFeedersAsync(
        string stopId,
        double radiusMeters = 3500,
        CancellationToken cancellationToken = default)
    {
        var stop = _gtfsService.GetStopById(stopId);
        if (stop == null) return new List<GtfsRealtimeVehicleDto>();

        // Query MRT Feeder buses near this station
        var feederBuses = await GetNearbyVehiclesAsync(
            stop.Latitude,
            stop.Longitude,
            radiusMeters,
            agency: "prasarana",
            category: "rapid-bus-mrtfeeder",
            cancellationToken: cancellationToken);

        // Also query KTMB if it's a commuter hub
        if (stop.StopName.Contains("Subang Jaya", StringComparison.OrdinalIgnoreCase) ||
            stop.StopName.Contains("KL Sentral", StringComparison.OrdinalIgnoreCase) ||
            stop.StopName.Contains("Bandar Tasik Selatan", StringComparison.OrdinalIgnoreCase))
        {
            var trains = await GetNearbyVehiclesAsync(
                stop.Latitude,
                stop.Longitude,
                radiusMeters * 2,
                agency: "ktmb",
                category: null,
                cancellationToken: cancellationToken);

            feederBuses.AddRange(trains);
            feederBuses = feederBuses.OrderBy(v => v.DistanceMeters).ToList();
        }

        return feederBuses;
    }

    private async Task<GtfsRealtimeFeedResponseDto> FetchAndParseFeedAsync(
        string agency,
        string? category,
        CancellationToken cancellationToken)
    {
        var requestUrl = BuildRequestUrl(agency, category);
        _logger.LogInformation("Fetching GTFS Realtime feed from {Url}", requestUrl);

        try
        {
            using var response = await _httpClient.GetAsync(requestUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var feedMessage = FeedMessage.Parser.ParseFrom(stream);

            var vehicles = new List<GtfsRealtimeVehicleDto>();

            foreach (var entity in feedMessage.Entity)
            {
                if (entity.Vehicle == null || entity.Vehicle.Position == null) continue;

                var v = entity.Vehicle;
                var pos = v.Position;

                var dto = new GtfsRealtimeVehicleDto
                {
                    Id = entity.Id,
                    VehicleId = v.Vehicle?.Id ?? entity.Id,
                    LicensePlate = !string.IsNullOrEmpty(v.Vehicle?.LicensePlate) ? v.Vehicle.LicensePlate : null,
                    Label = v.Vehicle?.Label,
                    TripId = v.Trip?.TripId,
                    RouteId = v.Trip?.RouteId,
                    Agency = agency,
                    Category = category,
                    Latitude = pos.Latitude,
                    Longitude = pos.Longitude,
                    Bearing = pos.HasBearing ? pos.Bearing : null,
                    SpeedKmh = pos.HasSpeed ? Math.Round(pos.Speed * 3.6, 1) : null, // m/s to km/h
                    Odometer = pos.HasOdometer ? pos.Odometer : null,
                    CurrentStatus = v.HasCurrentStatus ? v.CurrentStatus.ToString() : null,
                    StopId = v.StopId,
                    CurrentStopSequence = v.HasCurrentStopSequence ? v.CurrentStopSequence : null,
                    CongestionLevel = v.HasCongestionLevel ? v.CongestionLevel.ToString() : null,
                    Timestamp = v.HasTimestamp ? DateTimeOffset.FromUnixTimeSeconds((long)v.Timestamp).UtcDateTime : DateTime.UtcNow
                };

                vehicles.Add(dto);
            }

            var feedTimestamp = feedMessage.Header?.Timestamp ?? (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            return new GtfsRealtimeFeedResponseDto
            {
                Agency = agency,
                Category = category,
                FeedTimestamp = (long)feedTimestamp,
                UpdatedAt = DateTimeOffset.FromUnixTimeSeconds((long)feedTimestamp).UtcDateTime,
                TotalVehicles = vehicles.Count,
                Vehicles = vehicles
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch or parse GTFS Realtime feed from {Url}", requestUrl);

            // Fallback empty response to prevent downstream frontend crash
            return new GtfsRealtimeFeedResponseDto
            {
                Agency = agency,
                Category = category,
                FeedTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                UpdatedAt = DateTime.UtcNow,
                TotalVehicles = 0,
                Vehicles = new List<GtfsRealtimeVehicleDto>()
            };
        }
    }

    private static string BuildRequestUrl(string agency, string? category)
    {
        // Trailing slash is essential to avoid 301 redirect overhead on api.data.gov.my
        if (string.IsNullOrWhiteSpace(category))
        {
            return $"{BaseApiUrl}/{agency}/";
        }

        return $"{BaseApiUrl}/{agency}/?category={Uri.EscapeDataString(category)}";
    }
}
