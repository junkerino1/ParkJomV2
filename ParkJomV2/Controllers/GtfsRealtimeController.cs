using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkJomV2.DTOs;
using ParkJomV2.Services;

namespace ParkJomV2.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/gtfs/realtime")]
public class GtfsRealtimeController : ControllerBase
{
    private readonly GtfsRealtimeService _realtimeService;
    private readonly ILogger<GtfsRealtimeController> _logger;

    public GtfsRealtimeController(
        GtfsRealtimeService realtimeService,
        ILogger<GtfsRealtimeController> logger)
    {
        _realtimeService = realtimeService;
        _logger = logger;
    }

    /// <summary>
    /// RT-01: List supported Malaysian GTFS-Realtime transit agencies and categories (KTMB, Prasarana Rapid KL, myBAS).
    /// </summary>
    [HttpGet("agencies")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> GetAgencies()
    {
        var agencies = _realtimeService.GetSupportedAgencies();
        return Ok(new
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Supported GTFS-Realtime agencies retrieved.",
            Data = agencies
        });
    }

    /// <summary>
    /// RT-02: Get live vehicle positions from Malaysia's official open API (data.gov.my).
    /// Agency: 'prasarana' (default), 'ktmb', 'mybas-johor'.
    /// Category for Prasarana: 'rapid-bus-mrtfeeder' (default), 'rapid-bus-kl', 'rapid-bus-penang', 'rapid-bus-kuantan'.
    /// Optional routeId filter (e.g. 'T778', 'T801', '750').
    /// </summary>
    [HttpGet("vehicles")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<object>> GetLiveVehicles(
        [FromQuery] string agency = "prasarana",
        [FromQuery] string? category = "rapid-bus-mrtfeeder",
        [FromQuery] string? routeId = null,
        CancellationToken cancellationToken = default)
    {
        var feed = await _realtimeService.GetVehiclePositionsAsync(agency, category, cancellationToken);

        var vehicles = feed.Vehicles;
        if (!string.IsNullOrWhiteSpace(routeId))
        {
            var r = routeId.Trim();
            vehicles = vehicles.Where(v => string.Equals(v.RouteId, r, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return Ok(new
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            FeedTimestamp = feed.FeedTimestamp,
            UpdatedAt = feed.UpdatedAt,
            TotalVehicles = vehicles.Count,
            Message = $"Retrieved {vehicles.Count} live vehicle(s) for agency '{agency}'.",
            Data = vehicles
        });
    }

    /// <summary>
    /// RT-03: Get live vehicle positions as GeoJSON FeatureCollection for Leaflet / Mapbox map rendering.
    /// Passes directly to L.geoJSON(data) in React / Web.
    /// </summary>
    [HttpGet("vehicles/geojson")]
    [Produces("application/geo+json", "application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLiveVehiclesGeoJson(
        [FromQuery] string agency = "prasarana",
        [FromQuery] string? category = "rapid-bus-mrtfeeder",
        CancellationToken cancellationToken = default)
    {
        var geoJson = await _realtimeService.GetVehiclesGeoJsonAsync(agency, category, cancellationToken);
        return Ok(geoJson);
    }

    /// <summary>
    /// RT-04: Find live transit vehicles near commuter GPS coordinates (lat, lng) with distance and estimated arrival time.
    /// </summary>
    [HttpGet("vehicles/nearby")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<object>> GetNearbyVehicles(
        [FromQuery] double? lat,
        [FromQuery] double? lng,
        [FromQuery] double radiusMeters = 3000,
        [FromQuery] string agency = "prasarana",
        [FromQuery] string? category = "rapid-bus-mrtfeeder",
        CancellationToken cancellationToken = default)
    {
        if (!lat.HasValue || !lng.HasValue)
        {
            return BadRequest(new
            {
                Code = StatusCodes.Status400BadRequest,
                Success = false,
                Message = "Latitude ('lat') and Longitude ('lng') are required query parameters."
            });
        }

        var vehicles = await _realtimeService.GetNearbyVehiclesAsync(
            lat.Value,
            lng.Value,
            radiusMeters,
            agency,
            category,
            cancellationToken);

        return Ok(new
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            TotalNearby = vehicles.Count,
            RadiusMeters = radiusMeters,
            Message = $"Found {vehicles.Count} live vehicle(s) within {radiusMeters}m.",
            Data = vehicles
        });
    }

    /// <summary>
    /// RT-05: Get live approaching MRT Feeder buses and connecting trains for a specific station (e.g. 'KJ28', 'KG09', 'PY15').
    /// </summary>
    [HttpGet("stops/{stopId}/feeders")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<object>> GetStationFeeders(
        string stopId,
        [FromQuery] double radiusMeters = 3500,
        CancellationToken cancellationToken = default)
    {
        var feeders = await _realtimeService.GetStationFeedersAsync(stopId, radiusMeters, cancellationToken);

        return Ok(new
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            StopId = stopId,
            RadiusMeters = radiusMeters,
            TotalApproaching = feeders.Count,
            Message = $"Found {feeders.Count} live feeder vehicle(s) near station '{stopId}'.",
            Data = feeders
        });
    }
}
