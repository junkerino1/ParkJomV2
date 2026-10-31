using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.Services.Copilot;

/// <summary>
/// Declares the tools exposed to Gemini and executes them against existing ParkJom services.
/// Token &amp; RPD optimizations:
/// - <c>findStationsNear</c> and <c>searchStations</c> automatically attach available ParkJom bays
///   so the model resolves station + parking in 1 tool round instead of 3.
/// - Tool results returned to Gemini are compact (only fields needed for text generation),
///   while full details (addresses, images) go directly into <see cref="Cards"/> for the React UI.
/// </summary>
public class CopilotToolExecutor
{
    private const int MaxParkingResults = 4;
    private static readonly TimeZoneInfo KlTimeZone = ResolveKlTimeZone();

    private readonly GtfsService _gtfs;
    private readonly NominatimService _nominatim;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<CopilotToolExecutor> _logger;

    public CopilotToolExecutor(
        GtfsService gtfs,
        NominatimService nominatim,
        ApplicationDbContext db,
        ILogger<CopilotToolExecutor> logger)
    {
        _gtfs = gtfs;
        _nominatim = nominatim;
        _db = db;
        _logger = logger;
    }

    /// <summary>Cards emitted during one chat request (rendered by the frontend).</summary>
    public List<CopilotCardDto> Cards { get; } = new();

    private readonly StringBuilder _context = new();
    private readonly HashSet<string> _cardKeys = new();

    /// <summary>Compact summary of IDs shown this turn, returned to the client for follow-ups.</summary>
    public string? Context => _context.Length == 0 ? null : _context.ToString().Trim();

    public static DateOnly TodayInKl() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, KlTimeZone));
    public static DateTime NowInKl() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, KlTimeZone);

    public static JsonArray Declarations() => new()
    {
        Fn("geocodePlace",
            "Geocode a Malaysian place/landmark (e.g. 'KLCC', 'USJ Subang') to lat/lng.",
            Props(("query", Str("Place name in Malaysia."))),
            "query"),
        Fn("findStationsNear",
            "Find rail stations near lat/lng AND automatically list available ParkJom parking bays near them.",
            Props(
                ("lat", Num("Latitude.")),
                ("lng", Num("Longitude.")),
                ("radiusMeters", Num("Radius in meters, default 3000.")),
                ("limit", Num("Max stations, default 4."))),
            "lat", "lng"),
        Fn("searchStations",
            "Search rail stations by name or line ('KJ','AG','SP','KG','PY','MR','BRT') AND list available ParkJom bays near them.",
            Props(
                ("name", Str("Station name.")),
                ("line", Str("Line code or name."))),
            Array.Empty<string>()),
        Fn("getParkingNearStation",
            "Get available ParkJom bays near a specific station stopId.",
            Props(
                ("stopId", Str("Station stopId.")),
                ("radiusMeters", Num("Radius in meters, default 1500."))),
            "stopId"),
        Fn("getStationDepartures",
            "Get next estimated train departures and frequency at a station stopId.",
            Props(("stopId", Str("Station stopId."))),
            "stopId"),
        Fn("proposeBooking",
            "Show a booking proposal card (does not charge). Whole days only.",
            Props(
                ("parkingSpotId", Num("Bay ID.")),
                ("startDate", Str("yyyy-MM-dd.")),
                ("endDate", Str("yyyy-MM-dd inclusive."))),
            "parkingSpotId", "startDate", "endDate"),
    };

    public async Task<JsonNode> ExecuteAsync(string name, JsonObject? args, CancellationToken ct)
    {
        args ??= new JsonObject();
        try
        {
            return name switch
            {
                "geocodePlace" => await GeocodeAsync(args),
                "findStationsNear" => await FindStationsNearAsync(args),
                "searchStations" => await SearchStationsAsync(args),
                "getParkingNearStation" => await GetParkingNearStationAsync(args),
                "getStationDepartures" => GetStationDepartures(args),
                "proposeBooking" => await ProposeBookingAsync(args, ct),
                _ => Error($"Unknown tool '{name}'.")
            };
        }
        catch (ToolArgumentException ex)
        {
            return Error(ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Copilot tool {Tool} failed", name);
            return Error("Tool unavailable.");
        }
    }

    // ───────────────────────── Tools ─────────────────────────

    private async Task<JsonNode> GeocodeAsync(JsonObject args)
    {
        var query = RequireString(args, "query", 200);
        var results = await _nominatim.SearchAsync(query, 2);
        var places = new JsonArray();
        foreach (var r in results)
        {
            if (!double.TryParse(r.Lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) ||
                !double.TryParse(r.Lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var lng)) continue;
            places.Add(new JsonObject
            {
                ["name"] = string.IsNullOrWhiteSpace(r.PropertyName) ? query : r.PropertyName,
                ["lat"] = Math.Round(lat, 5),
                ["lng"] = Math.Round(lng, 5),
            });
        }
        return places.Count == 0
            ? Error($"No place found for '{query}'.")
            : new JsonObject { ["places"] = places };
    }

    private async Task<JsonNode> FindStationsNearAsync(JsonObject args)
    {
        var lat = RequireNumber(args, "lat", -90, 90);
        var lng = RequireNumber(args, "lng", -180, 180);
        var radius = OptionalNumber(args, "radiusMeters", 3500, 100, 10000);
        var limit = (int)OptionalNumber(args, "limit", 4, 1, 6);
        var stations = _gtfs.GetNearbyStations(lat, lng, radius, limit);
        if (stations.Count == 0)
            return Error("No stations within radius. Try radiusMeters=6000.");

        return await BuildStationsWithParkingAsync(stations);
    }

    private async Task<JsonNode> SearchStationsAsync(JsonObject args)
    {
        var name = OptionalString(args, "name", 100);
        var line = OptionalString(args, "line", 50);
        if (name == null && line == null) throw new ToolArgumentException("Provide 'name' or 'line'.");
        var stations = _gtfs.GetAllStops(line, name).Take(5).ToList();
        if (stations.Count == 0)
            return Error("No matching stations.");

        return await BuildStationsWithParkingAsync(stations);
    }

    private async Task<JsonNode> GetParkingNearStationAsync(JsonObject args)
    {
        var stopId = RequireString(args, "stopId", 50);
        var radius = OptionalNumber(args, "radiusMeters", 1500, 100, 3000);
        var data = await _gtfs.GetNearbyParkingAsync(stopId, radius, _db);
        if (data == null) return Error($"Unknown stopId '{stopId}'.");

        var stop = _gtfs.GetStopById(stopId);
        var bays = EmitParkingBays(data, stop?.RouteName);

        return new JsonObject
        {
            ["station"] = data.StopName,
            ["stopId"] = data.StopId,
            ["bays"] = bays,
        };
    }

    private JsonNode GetStationDepartures(JsonObject args)
    {
        var stopId = RequireString(args, "stopId", 50);
        var schedule = _gtfs.GetStopSchedule(stopId);
        if (schedule == null) return Error($"Unknown stopId '{stopId}'.");

        var fullDepartures = new JsonArray();
        var slimDepartures = new JsonArray();
        foreach (var d in schedule.Departures)
        {
            fullDepartures.Add(new JsonObject
            {
                ["direction"] = d.Direction,
                ["next"] = d.EstimatedDeparture,
                ["minutesUntil"] = d.MinutesUntilArrival,
                ["following"] = new JsonArray(d.SubsequentDepartures.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray()),
            });
            slimDepartures.Add(new JsonObject
            {
                ["dir"] = d.Direction,
                ["next"] = d.EstimatedDeparture,
                ["inMin"] = d.MinutesUntilArrival,
            });
        }

        AddCard("departures", $"departures:{schedule.StopId}", new JsonObject
        {
            ["stopId"] = schedule.StopId,
            ["stationName"] = schedule.StopName,
            ["lineName"] = schedule.RouteName,
            ["currentTime"] = schedule.CurrentTime,
            ["isOperating"] = schedule.IsOperating,
            ["serviceStatus"] = schedule.ServiceStatus,
            ["isPeakHour"] = schedule.IsPeakHour,
            ["frequencyMinutes"] = schedule.FrequencyMinutes,
            ["firstTrain"] = schedule.FirstTrain,
            ["lastTrain"] = schedule.LastTrain,
            ["departures"] = fullDepartures,
        });

        // Compact payload for the LLM to save tokens.
        return new JsonObject
        {
            ["station"] = schedule.StopName,
            ["line"] = schedule.RouteName,
            ["open"] = schedule.IsOperating,
            ["everyMin"] = schedule.FrequencyMinutes,
            ["nextTrains"] = slimDepartures,
        };
    }

    private async Task<JsonNode> ProposeBookingAsync(JsonObject args, CancellationToken ct)
    {
        var spotId = (int)RequireNumber(args, "parkingSpotId", 1, int.MaxValue);
        var start = RequireDate(args, "startDate");
        var end = RequireDate(args, "endDate");
        var today = TodayInKl();

        if (start < today) return Error($"startDate is in the past. Today is {today:yyyy-MM-dd}.");
        if (end < start) return Error("endDate must be >= startDate.");
        var days = end.DayNumber - start.DayNumber + 1;
        if (days > 31) return Error("Max 31 days.");

        var spot = await _db.ParkingSpots
            .AsNoTracking()
            .Where(s => s.ParkingSpotId == spotId)
            .Select(s => new
            {
                s.ParkingSpotId,
                s.ParkingLabel,
                s.DailyRate,
                s.IsPublished,
                s.AvailabilityStatus,
                PropertyName = s.Property.PropertyName,
                Address = s.Property.Address,
            })
            .FirstOrDefaultAsync(ct);

        if (spot == null || !spot.IsPublished || spot.AvailabilityStatus != AvailabilityStatus.Available)
            return Error($"Bay {spotId} is not available.");

        var total = spot.DailyRate.HasValue ? spot.DailyRate.Value * days : (decimal?)null;
        var card = new JsonObject
        {
            ["parkingSpotId"] = spot.ParkingSpotId,
            ["parkingLabel"] = spot.ParkingLabel ?? $"Bay {spot.ParkingSpotId}",
            ["propertyName"] = spot.PropertyName,
            ["address"] = spot.Address,
            ["startDate"] = start.ToString("yyyy-MM-dd"),
            ["endDate"] = end.ToString("yyyy-MM-dd"),
            ["days"] = days,
            ["dailyRate"] = spot.DailyRate,
            ["estimatedSubtotal"] = total,
        };
        AddCard("bookingProposal", $"proposal:{spotId}:{start}:{end}", card);
        _context.Append($"Proposed bay#{spotId} {start:yyyy-MM-dd}..{end:yyyy-MM-dd}. ");

        return new JsonObject
        {
            ["status"] = "proposal_card_shown",
            ["bayId"] = spot.ParkingSpotId,
            ["property"] = spot.PropertyName,
            ["days"] = days,
            ["estRm"] = total,
        };
    }

    // ───────────────────────── Helpers ─────────────────────────

    /// <summary>
    /// Bundles stations + their nearby available ParkJom bays in a single tool round
    /// so the LLM does not need to make 3 separate follow-up calls.
    /// </summary>
    private async Task<JsonObject> BuildStationsWithParkingAsync(List<GtfsStopDto> stations)
    {
        var stationArr = new JsonArray();
        var allBays = new JsonArray();
        var shownStations = new List<string>();

        foreach (var s in stations)
        {
            stationArr.Add(new JsonObject
            {
                ["stopId"] = s.StopId,
                ["name"] = s.StopName,
                ["line"] = s.RouteName,
                ["distM"] = s.DistanceMeters,
            });
            shownStations.Add($"{s.StopName}[{s.StopId}]");

            if (Cards.Count < MaxParkingResults)
            {
                var parking = await _gtfs.GetNearbyParkingAsync(s.StopId, 1500, _db);
                if (parking != null && parking.ParkingSpots.Count > 0)
                {
                    foreach (var bayNode in EmitParkingBays(parking, s.RouteName))
                        allBays.Add(bayNode?.DeepClone());
                }
            }
        }

        if (shownStations.Count > 0)
            _context.Append($"Stations: {string.Join(", ", shownStations)}. ");

        return new JsonObject
        {
            ["stations"] = stationArr,
            ["availableBays"] = allBays,
            ["hint"] = allBays.Count > 0
                ? "Available bays are already included in availableBays above; no need to call getParkingNearStation again for these stations."
                : "No ParkJom bays within 1.5km of these stations.",
        };
    }

    private JsonArray EmitParkingBays(GtfsNearbyParkingDto data, string? lineName)
    {
        var top = data.ParkingSpots.Take(MaxParkingResults - Cards.Count).ToList();
        var slimBays = new JsonArray();

        foreach (var spot in top)
        {
            slimBays.Add(new JsonObject
            {
                ["bayId"] = spot.ParkingSpotId,
                ["label"] = spot.ParkingLabel,
                ["property"] = spot.PropertyName,
                ["station"] = data.StopName,
                ["stopId"] = data.StopId,
                ["walkMin"] = spot.WalkingTimeMinutes,
                ["rmDay"] = spot.DailyRate,
            });

            AddCard("parking", $"parking:{spot.ParkingSpotId}", new JsonObject
            {
                ["parkingSpotId"] = spot.ParkingSpotId,
                ["parkingLabel"] = spot.ParkingLabel,
                ["propertyName"] = spot.PropertyName,
                ["address"] = spot.Address,
                ["distanceToStationMeters"] = spot.DistanceMeters,
                ["walkingTimeMinutes"] = spot.WalkingTimeMinutes,
                ["dailyRate"] = spot.DailyRate,
                ["monthlyRate"] = spot.MonthlyRate,
                ["availabilityStatus"] = spot.AvailabilityStatus,
                ["hasSmartBollard"] = spot.HasIoTDevice,
                ["stationName"] = data.StopName,
                ["stopId"] = data.StopId,
                ["lineName"] = lineName,
                ["primaryImageUrl"] = spot.PrimaryImageUrl,
            });
        }

        if (top.Count > 0)
        {
            _context.Append($"Bays@{data.StopName}[{data.StopId}]: ");
            _context.AppendJoin(", ", top.Select(s => $"id={s.ParkingSpotId} '{s.ParkingLabel}' ({s.PropertyName}, RM{s.DailyRate:0.##}/d)"));
            _context.Append(". ");
        }

        return slimBays;
    }

    private void AddCard(string type, string key, JsonNode data)
    {
        if (_cardKeys.Add(key)) Cards.Add(new CopilotCardDto { Type = type, Data = data });
    }

    private static JsonObject Error(string message) => new() { ["error"] = message };

    private static string RequireString(JsonObject args, string key, int maxLength) =>
        OptionalString(args, key, maxLength) ?? throw new ToolArgumentException($"'{key}' is required.");

    private static string? OptionalString(JsonObject args, string key, int maxLength)
    {
        var node = args[key];
        if (node is not JsonValue v || !v.TryGetValue<string>(out var s) || string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        return s.Length > maxLength ? s[..maxLength] : s;
    }

    private static double RequireNumber(JsonObject args, string key, double min, double max) =>
        TryNumber(args, key, out var value)
            ? (value < min || value > max ? throw new ToolArgumentException($"'{key}' is out of range.") : value)
            : throw new ToolArgumentException($"'{key}' is required.");

    private static double OptionalNumber(JsonObject args, string key, double fallback, double min, double max) =>
        TryNumber(args, key, out var value) ? Math.Clamp(value, min, max) : fallback;

    private static bool TryNumber(JsonObject args, string key, out double value)
    {
        value = 0;
        if (args[key] is not JsonValue v) return false;
        if (v.TryGetValue<double>(out value)) return double.IsFinite(value);
        return v.TryGetValue<string>(out var s)
               && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
               && double.IsFinite(value);
    }

    private static DateOnly RequireDate(JsonObject args, string key)
    {
        var s = RequireString(args, key, 10);
        return DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : throw new ToolArgumentException($"'{key}' must be yyyy-MM-dd.");
    }

    private static JsonObject Fn(string name, string description, JsonObject properties, params string[] required)
    {
        var parameters = new JsonObject { ["type"] = "object", ["properties"] = properties };
        if (required.Length > 0)
            parameters["required"] = new JsonArray(required.Select(r => (JsonNode?)JsonValue.Create(r)).ToArray());
        return new JsonObject { ["name"] = name, ["description"] = description, ["parameters"] = parameters };
    }

    private static JsonObject Props(params (string Name, JsonObject Schema)[] props)
    {
        var obj = new JsonObject();
        foreach (var (n, schema) in props) obj[n] = schema;
        return obj;
    }

    private static JsonObject Str(string description) => new() { ["type"] = "string", ["description"] = description };
    private static JsonObject Num(string description) => new() { ["type"] = "number", ["description"] = description };

    private static TimeZoneInfo ResolveKlTimeZone()
    {
        foreach (var id in new[] { "Asia/Kuala_Lumpur", "Singapore Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("MYT", TimeSpan.FromHours(8), "MYT", "MYT");
    }

    private sealed class ToolArgumentException(string message) : Exception(message);
}
