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
/// Every tool is read-only: <c>proposeBooking</c> only validates and returns a UI card —
/// the commuter must confirm through the normal quote/confirm booking flow.
/// </summary>
public class CopilotToolExecutor
{
    private const int MaxParkingResults = 5;
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
            "Convert a Malaysian place name, landmark, or address (e.g. 'KLCC', 'USJ Subang', 'Sunway Pyramid') into coordinates.",
            Props(("query", Str("Place name or address in Malaysia."))),
            "query"),
        Fn("findStationsNear",
            "Find Klang Valley rail stations (LRT, MRT, Monorail, KTM, BRT) near a coordinate. Interchange stations appear once per line.",
            Props(
                ("lat", Num("Latitude.")),
                ("lng", Num("Longitude.")),
                ("radiusMeters", Num("Search radius in meters, default 3000, max 10000.")),
                ("limit", Num("Max stations, default 5, max 10."))),
            "lat", "lng"),
        Fn("searchStations",
            "Search rail stations by name and/or line. Line examples: 'KJ' (LRT Kelana Jaya), 'AG' (LRT Ampang), 'SP' (LRT Sri Petaling), 'KG' (MRT Kajang), 'PY' (MRT Putrajaya), 'MR' (Monorail).",
            Props(
                ("name", Str("Full or partial station name.")),
                ("line", Str("Optional line code or name."))),
            Array.Empty<string>()),
        Fn("getParkingNearStation",
            "List currently available, verified ParkJom parking bays near a station. Returns daily/monthly rates and walking time to the station.",
            Props(
                ("stopId", Str("Station stopId from findStationsNear or searchStations.")),
                ("radiusMeters", Num("Search radius in meters, default 1500, max 3000."))),
            "stopId"),
        Fn("getStationDepartures",
            "Get the estimated next train departures for a station, by direction, plus peak/off-peak frequency.",
            Props(("stopId", Str("Station stopId."))),
            "stopId"),
        Fn("proposeBooking",
            "Propose a booking for the commuter to review. This does NOT book or charge anything; it shows a card with a Reserve button. Bookings are whole days.",
            Props(
                ("parkingSpotId", Num("Parking bay ID from getParkingNearStation.")),
                ("startDate", Str("First day, yyyy-MM-dd (Malaysia time).")),
                ("endDate", Str("Last day inclusive, yyyy-MM-dd. Same as startDate for a single day."))),
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
                "findStationsNear" => FindStationsNear(args),
                "searchStations" => SearchStations(args),
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
            return Error("The tool failed. Tell the user this information is temporarily unavailable.");
        }
    }

    // ───────────────────────── Tools ─────────────────────────

    private async Task<JsonNode> GeocodeAsync(JsonObject args)
    {
        var query = RequireString(args, "query", 200);
        var results = await _nominatim.SearchAsync(query, 3);
        var places = new JsonArray();
        foreach (var r in results)
        {
            if (!double.TryParse(r.Lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) ||
                !double.TryParse(r.Lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var lng)) continue;
            places.Add(new JsonObject
            {
                ["name"] = string.IsNullOrWhiteSpace(r.PropertyName) ? query : r.PropertyName,
                ["address"] = r.Address,
                ["lat"] = Math.Round(lat, 6),
                ["lng"] = Math.Round(lng, 6),
            });
        }
        return places.Count == 0
            ? Error($"No place found for '{query}'. Ask the user to be more specific.")
            : new JsonObject { ["places"] = places };
    }

    private JsonNode FindStationsNear(JsonObject args)
    {
        var lat = RequireNumber(args, "lat", -90, 90);
        var lng = RequireNumber(args, "lng", -180, 180);
        var radius = OptionalNumber(args, "radiusMeters", 3000, 100, 10000);
        var limit = (int)OptionalNumber(args, "limit", 5, 1, 10);
        var stations = _gtfs.GetNearbyStations(lat, lng, radius, limit);
        return stations.Count == 0
            ? Error("No rail stations within that radius. Try a larger radius.")
            : new JsonObject { ["stations"] = StationsToJson(stations) };
    }

    private JsonNode SearchStations(JsonObject args)
    {
        var name = OptionalString(args, "name", 100);
        var line = OptionalString(args, "line", 50);
        if (name == null && line == null) throw new ToolArgumentException("Provide 'name' or 'line'.");
        var stations = _gtfs.GetAllStops(line, name).Take(10).ToList();
        return stations.Count == 0
            ? Error("No matching stations.")
            : new JsonObject { ["stations"] = StationsToJson(stations) };
    }

    private async Task<JsonNode> GetParkingNearStationAsync(JsonObject args)
    {
        var stopId = RequireString(args, "stopId", 50);
        var radius = OptionalNumber(args, "radiusMeters", 1500, 100, 3000);
        var data = await _gtfs.GetNearbyParkingAsync(stopId, radius, _db);
        if (data == null) return Error($"Unknown stopId '{stopId}'.");

        var stop = _gtfs.GetStopById(stopId);
        var top = data.ParkingSpots.Take(MaxParkingResults).ToList();
        var bays = new JsonArray();

        foreach (var spot in top)
        {
            var item = new JsonObject
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
            };
            bays.Add(item);

            var card = item.DeepClone().AsObject();
            card["stationName"] = data.StopName;
            card["stopId"] = data.StopId;
            card["lineName"] = stop?.RouteName;
            card["primaryImageUrl"] = spot.PrimaryImageUrl;
            AddCard("parking", $"parking:{spot.ParkingSpotId}", card);
        }

        if (top.Count > 0)
        {
            _context.Append($"Bays shown near {data.StopName} [{data.StopId}]: ");
            _context.AppendJoin("; ", top.Select((s, i) =>
                $"#{i + 1} id={s.ParkingSpotId} '{s.ParkingLabel}' at {s.PropertyName} RM{s.DailyRate?.ToString("0.00", CultureInfo.InvariantCulture) ?? "-"}/day"));
            _context.Append(". ");
        }

        return new JsonObject
        {
            ["stationName"] = data.StopName,
            ["stopId"] = data.StopId,
            ["totalAvailableBays"] = data.TotalAvailableSpots,
            ["bays"] = bays,
            ["note"] = data.TotalAvailableSpots == 0
                ? "No available ParkJom bays near this station. Suggest a neighbouring station on the same line."
                : null,
        };
    }

    private JsonNode GetStationDepartures(JsonObject args)
    {
        var stopId = RequireString(args, "stopId", 50);
        var schedule = _gtfs.GetStopSchedule(stopId);
        if (schedule == null) return Error($"Unknown stopId '{stopId}'.");

        var departures = new JsonArray();
        foreach (var d in schedule.Departures)
        {
            departures.Add(new JsonObject
            {
                ["direction"] = d.Direction,
                ["next"] = d.EstimatedDeparture,
                ["minutesUntil"] = d.MinutesUntilArrival,
                ["following"] = new JsonArray(d.SubsequentDepartures.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray()),
            });
        }

        var result = new JsonObject
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
            ["departures"] = departures,
            ["note"] = "Departures are estimates based on the line's frequency, not live tracking.",
        };

        AddCard("departures", $"departures:{schedule.StopId}", result.DeepClone());
        return result;
    }

    private async Task<JsonNode> ProposeBookingAsync(JsonObject args, CancellationToken ct)
    {
        var spotId = (int)RequireNumber(args, "parkingSpotId", 1, int.MaxValue);
        var start = RequireDate(args, "startDate");
        var end = RequireDate(args, "endDate");
        var today = TodayInKl();

        if (start < today) return Error($"startDate is in the past. Today is {today:yyyy-MM-dd}.");
        if (end < start) return Error("endDate must be on or after startDate.");
        var days = end.DayNumber - start.DayNumber + 1;
        if (days > 31) return Error("Bookings proposed by the assistant are limited to 31 days.");

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

        if (spot == null || !spot.IsPublished) return Error($"Parking bay {spotId} does not exist or is not bookable.");
        if (spot.AvailabilityStatus != AvailabilityStatus.Available) return Error($"Parking bay {spotId} is not currently available.");

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
            ["estimatedSubtotal"] = spot.DailyRate.HasValue ? spot.DailyRate.Value * days : null,
        };
        AddCard("bookingProposal", $"proposal:{spotId}:{start}:{end}", card);
        _context.Append($"Proposed booking: bay id={spotId} {start:yyyy-MM-dd} to {end:yyyy-MM-dd}. ");

        return new JsonObject
        {
            ["status"] = "proposal_shown",
            ["instruction"] = "Tell the user to review the card and tap Reserve. Final price including platform fee is shown on the quote. Do not claim it is booked.",
            ["proposal"] = card.DeepClone(),
        };
    }

    // ───────────────────────── Helpers ─────────────────────────

    private void AddCard(string type, string key, JsonNode data)
    {
        if (_cardKeys.Add(key)) Cards.Add(new CopilotCardDto { Type = type, Data = data });
    }

    private JsonArray StationsToJson(IEnumerable<GtfsStopDto> stations)
    {
        var arr = new JsonArray();
        var shown = new List<string>();
        foreach (var s in stations)
        {
            arr.Add(new JsonObject
            {
                ["stopId"] = s.StopId,
                ["name"] = s.StopName,
                ["line"] = s.RouteName,
                ["lineId"] = s.RouteId,
                ["category"] = s.Category,
                ["lat"] = s.Latitude,
                ["lng"] = s.Longitude,
                ["distanceMeters"] = s.DistanceMeters,
            });
            shown.Add($"{s.StopName} [{s.StopId}, {s.RouteName}]");
        }
        if (shown.Count > 0) _context.Append($"Stations found: {string.Join("; ", shown)}. ");
        return arr;
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
            : throw new ToolArgumentException($"'{key}' is required and must be a number.");

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
