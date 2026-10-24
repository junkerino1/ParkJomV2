using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Services;

namespace ParkJomV2.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/gtfs")]
public class GtfsController : ControllerBase
{
    private readonly GtfsService _gtfsService;
    private readonly ApplicationDbContext _dbContext;
    private readonly AccessLogService _accessLogService;
    private readonly ILogger<GtfsController> _logger;

    public GtfsController(
        GtfsService gtfsService,
        ApplicationDbContext dbContext,
        AccessLogService accessLogService,
        ILogger<GtfsController> logger)
    {
        _gtfsService = gtfsService;
        _dbContext = dbContext;
        _accessLogService = accessLogService;
        _logger = logger;
    }

    /// <summary>
    /// G-01: Get all transit routes / rail lines in Klang Valley (LRT, MRT, Monorail, BRT).
    /// </summary>
    [HttpGet("routes")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> GetAllRoutes()
    {
        var routes = _gtfsService.GetAllRoutes();
        return Ok(new
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = $"Retrieved {routes.Count} transit routes.",
            Data = routes
        });
    }

    /// <summary>
    /// G-02: Get route details and ordered sequence of stops by route ID (e.g. KJ, KGL, PYL, AG, SP, MR, BRT, SA).
    /// </summary>
    [HttpGet("routes/{routeId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<object> GetRouteById(string routeId)
    {
        var route = _gtfsService.GetRouteById(routeId);
        if (route == null)
        {
            return NotFound(new
            {
                Code = StatusCodes.Status404NotFound,
                Success = false,
                Message = $"Route '{routeId}' not found."
            });
        }

        return Ok(new
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Route details retrieved successfully.",
            Data = route
        });
    }

    /// <summary>
    /// G-03: Get GeoJSON LineString coordinates for a route (for Leaflet/Mapbox railway polyline rendering).
    /// </summary>
    [HttpGet("routes/{routeId}/shape")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<object> GetRouteShape(string routeId)
    {
        var shape = _gtfsService.GetRouteShape(routeId);
        if (shape == null)
        {
            return NotFound(new
            {
                Code = StatusCodes.Status404NotFound,
                Success = false,
                Message = $"Route shape for '{routeId}' not found."
            });
        }

        return Ok(new
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Route shape coordinates retrieved successfully.",
            Data = shape
        });
    }

    /// <summary>
    /// G-04: Get transit stops / stations with optional line filter, search keyword, or nearby GPS radius.
    /// </summary>
    [HttpGet("stops")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> GetAllStops(
        [FromQuery] string? line = null,
        [FromQuery] string? search = null,
        [FromQuery] double? lat = null,
        [FromQuery] double? lng = null,
        [FromQuery] double? radiusMeters = null)
    {
        var stops = _gtfsService.GetAllStops(line, search, lat, lng, radiusMeters);
        return Ok(new
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            TotalCount = stops.Count,
            Message = $"Retrieved {stops.Count} stops.",
            Data = stops
        });
    }

    /// <summary>
    /// G-05: Get stop details by stop code (e.g. KJ28, SP12, KG09) with multilingual names and accessibility.
    /// </summary>
    [HttpGet("stops/{stopId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<object> GetStopById(string stopId)
    {
        var stop = _gtfsService.GetStopById(stopId);
        if (stop == null)
        {
            return NotFound(new
            {
                Code = StatusCodes.Status404NotFound,
                Success = false,
                Message = $"Stop '{stopId}' not found."
            });
        }

        return Ok(new
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Stop details retrieved successfully.",
            Data = stop
        });
    }

    /// <summary>
    /// G-06: Get live estimated timetable, peak/off-peak frequencies, first/last train timings, and upcoming departures.
    /// </summary>
    [HttpGet("stops/{stopId}/schedule")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<object> GetStopSchedule(string stopId)
    {
        var schedule = _gtfsService.GetStopSchedule(stopId);
        if (schedule == null)
        {
            return NotFound(new
            {
                Code = StatusCodes.Status404NotFound,
                Success = false,
                Message = $"Stop '{stopId}' not found."
            });
        }

        return Ok(new
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Stop schedule and upcoming departures retrieved successfully.",
            Data = schedule
        });
    }

    /// <summary>
    /// G-07: Find verified, published ParkJom parking spots within walking distance of this transit station.
    /// </summary>
    [HttpGet("stops/{stopId}/nearby-parking")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<object>> GetNearbyParking(
        string stopId,
        [FromQuery] double radiusMeters = 1500)
    {
        var result = await _gtfsService.GetNearbyParkingAsync(stopId, radiusMeters, _dbContext);
        if (result == null)
        {
            return NotFound(new
            {
                Code = StatusCodes.Status404NotFound,
                Success = false,
                Message = $"Stop '{stopId}' not found."
            });
        }

        return Ok(new
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = $"Found {result.TotalAvailableSpots} parking spot(s) near {result.StopName}.",
            Data = result
        });
    }

    /// <summary>
    /// G-08: Find nearest transit stations to commuter GPS coordinates (lat, lng) sorted by distance.
    /// </summary>
    [HttpGet("nearby-stations")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<object> GetNearbyStations(
        [FromQuery] double? lat,
        [FromQuery] double? lng,
        [FromQuery] double radiusMeters = 3000,
        [FromQuery] int limit = 10)
    {
        if (!lat.HasValue || !lng.HasValue)
        {
            return BadRequest(new
            {
                Code = StatusCodes.Status400BadRequest,
                Success = false,
                Message = "Latitude ('lat') and Longitude ('lng') query parameters are required."
            });
        }

        var stations = _gtfsService.GetNearbyStations(lat.Value, lng.Value, radiusMeters, limit);
        return Ok(new
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Count = stations.Count,
            Message = $"Found {stations.Count} station(s) within {radiusMeters}m.",
            Data = stations
        });
    }

    /// <summary>
    /// G-09: Raw GeoJSON FeatureCollection of all Klang Valley rail lines (for Leaflet map polyline layer).
    /// </summary>
    [HttpGet("geojson/lines")]
    [Produces("application/geo+json", "application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetGeoJsonLines()
    {
        var raw = _gtfsService.GetRawGeoJsonLines();
        if (string.IsNullOrEmpty(raw))
        {
            return NotFound(new { message = "Rail lines GeoJSON not loaded." });
        }
        return Content(raw, "application/geo+json");
    }

    /// <summary>
    /// G-10: Raw GeoJSON FeatureCollection of all Klang Valley rail stops (for Leaflet map marker layer).
    /// </summary>
    [HttpGet("geojson/stops")]
    [Produces("application/geo+json", "application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetGeoJsonStops()
    {
        var raw = _gtfsService.GetRawGeoJsonStops();
        if (string.IsNullOrEmpty(raw))
        {
            return NotFound(new { message = "Rail stops GeoJSON not loaded." });
        }
        return Content(raw, "application/geo+json");
    }
}
