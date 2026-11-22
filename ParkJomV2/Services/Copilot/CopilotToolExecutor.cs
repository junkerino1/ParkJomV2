using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services.Prediction;

namespace ParkJomV2.Services.Copilot;

/// <summary>
/// Declares and executes database, transit, availability, and prediction tools exposed to Gemini.
/// All queries use real database state (bookings, availability schedules, amenities, and public lots)
/// scoped safely to the authenticated caller.
/// </summary>
public class CopilotToolExecutor
{
    private const int MaxParkingCardsPerTurn = 4;
    private const double DefaultStationParkingRadiusMeters = 1500;
    private static readonly TimeZoneInfo KlTimeZone = ResolveKlTimeZone();

    private readonly GtfsService _gtfs;
    private readonly NominatimService _nominatim;
    private readonly ApplicationDbContext _db;
    private readonly PublicParkingService _publicParking;
    private readonly ParkingPredictorService _predictor;
    private readonly ILogger<CopilotToolExecutor> _logger;

    public CopilotToolExecutor(
        GtfsService gtfs,
        NominatimService nominatim,
        ApplicationDbContext db,
        PublicParkingService publicParking,
        ParkingPredictorService predictor,
        ILogger<CopilotToolExecutor> logger)
    {
        _gtfs = gtfs;
        _nominatim = nominatim;
        _db = db;
        _publicParking = publicParking;
        _predictor = predictor;
        _logger = logger;
    }

    /// <summary>Cards emitted during one chat request (rendered by the React UI).</summary>
    public List<CopilotCardDto> Cards { get; } = new();

    private readonly StringBuilder _context = new();
    private readonly HashSet<string> _cardKeys = new();

    /// <summary>Compact summary of IDs shown this turn, returned to the client for follow-ups.</summary>
    public string? Context => _context.Length == 0 ? null : _context.ToString().Trim();

    public static DateOnly TodayInKl() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, KlTimeZone));
    public static DateTime NowInKl() => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, KlTimeZone);

    public static JsonArray Declarations() => new()
    {
        Fn("searchParking",
            "Search ParkJom private bookable bays AND public drive-in parking lots by place/station/property name or lat/lng, with real date availability and amenity filters (covered, EV, CCTV, accessible, maxDailyRate).",
            Props(
                ("query", Str("Place, station, condo/property, or area name (e.g. 'Subang LRT', 'PV 9 Setapak', 'KLCC').")),
                ("lat", Num("Optional search center latitude.")),
                ("lng", Num("Optional search center longitude.")),
                ("radiusKm", Num("Search radius in km (default 5, max 50).")),
                ("date", Str("Optional start date yyyy-MM-dd to verify live availability.")),
                ("endDate", Str("Optional inclusive end date yyyy-MM-dd (defaults to date).")),
                ("includePrivateBays", Bool("Include private bookable bays (tomorrow or later). Default true.")),
                ("includePublicLots", Bool("Include public drive-in parking lots (usable today without advance booking). Default true.")),
                ("coveredOnly", Bool("Only return covered parking bays.")),
                ("evOnly", Bool("Only return bays with EV charging.")),
                ("cctvOnly", Bool("Only return bays with CCTV.")),
                ("accessibleOnly", Bool("Only return wheelchair-accessible bays.")),
                ("maxDailyRate", Num("Maximum daily rate in RM."))),
            Array.Empty<string>()),
        Fn("checkSpotAvailability",
            "Check live owner availability rules, existing bookings, open dates, and exact price estimate for a specific private bay ID.",
            Props(
                ("parkingSpotId", Num("Private parking bay ID.")),
                ("startDate", Str("Optional start date yyyy-MM-dd.")),
                ("endDate", Str("Optional inclusive end date yyyy-MM-dd."))),
            "parkingSpotId"),
        Fn("getMyBookings",
            "Get the signed-in user's upcoming, active, and pending parking bookings.",
            Props(),
            Array.Empty<string>()),
        Fn("geocodePlace",
            "Geocode a Malaysian place/landmark (e.g. 'KLCC', 'USJ Subang') to lat/lng.",
            Props(("query", Str("Place name in Malaysia."))),
            "query"),
        Fn("findStationsNear",
            "Find rail stations near lat/lng AND attach nearby private ParkJom bays + public drive-in lots.",
            Props(
                ("lat", Num("Latitude.")),
                ("lng", Num("Longitude.")),
                ("radiusMeters", Num("Radius in meters, default 3500.")),
                ("limit", Num("Max stations, default 4.")),
                ("date", Str("Optional date yyyy-MM-dd to check availability."))),
            "lat", "lng"),
        Fn("searchStations",
            "Search rail stations by name or line ('KJ','AG','SP','KG','PY','MR','BRT','SA') AND attach nearby private ParkJom bays + public drive-in lots.",
            Props(
                ("name", Str("Station name (e.g. 'Subang', 'Taman Melati', 'SS15').")),
                ("line", Str("Line code or name.")),
                ("date", Str("Optional date yyyy-MM-dd to check availability."))),
            Array.Empty<string>()),
        Fn("getParkingNearStation",
            "Get available private ParkJom bays and public drive-in lots near a specific station stopId.",
            Props(
                ("stopId", Str("Station stopId.")),
                ("radiusMeters", Num("Radius in meters, default 1500.")),
                ("date", Str("Optional date yyyy-MM-dd to check availability."))),
            "stopId"),
        Fn("getStationDepartures",
            "Get next estimated train departures and frequency at a station stopId.",
            Props(("stopId", Str("Station stopId."))),
            "stopId"),
        Fn("proposeBooking",
            "Verify live availability & pricing and show a booking proposal card (does not charge). Start date must be tomorrow or later; whole days only.",
            Props(
                ("parkingSpotId", Num("Private bay ID.")),
                ("startDate", Str("yyyy-MM-dd (tomorrow or later).")),
                ("endDate", Str("yyyy-MM-dd inclusive."))),
            "parkingSpotId", "startDate", "endDate"),
        Fn("predictParkingCongestion",
            "Predict parking bay congestion, occupancy %, probability of filling, weather impact, and recommended arrival time at a station stopId or parkingSpotId.",
            Props(
                ("stopId", Str("Station stopId (e.g. 'KJ10', 'KG9').")),
                ("parkingSpotId", Num("Optional specific parking bay ID."))),
            Array.Empty<string>()),
    };

    public async Task<JsonNode> ExecuteAsync(string name, JsonObject? args, int userId, CancellationToken ct)
    {
        args ??= new JsonObject();
        try
        {
            return name switch
            {
                "searchParking" => await SearchParkingAsync(args, userId, ct),
                "checkSpotAvailability" => await CheckSpotAvailabilityAsync(args, userId, ct),
                "getMyBookings" => await GetMyBookingsAsync(userId, ct),
                "geocodePlace" => await GeocodeAsync(args),
                "findStationsNear" => await FindStationsNearAsync(args, userId, ct),
                "searchStations" => await SearchStationsAsync(args, userId, ct),
                "getParkingNearStation" => await GetParkingNearStationAsync(args, userId, ct),
                "getStationDepartures" => GetStationDepartures(args),
                "proposeBooking" => await ProposeBookingAsync(args, userId, ct),
                "predictParkingCongestion" => await PredictParkingCongestionAsync(args, ct),
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

    private async Task<JsonNode> SearchParkingAsync(JsonObject args, int userId, CancellationToken ct)
    {
        var query = OptionalString(args, "query", 150);
        var hasLat = TryNumber(args, "lat", out var explicitLat);
        var hasLng = TryNumber(args, "lng", out var explicitLng);
        if (hasLat != hasLng)
            throw new ToolArgumentException("Provide both 'lat' and 'lng' when specifying coordinates.");
        if (hasLat && (explicitLat < -90 || explicitLat > 90 || explicitLng < -180 || explicitLng > 180))
            throw new ToolArgumentException("Coordinates are out of range.");

        var radiusKm = OptionalNumber(args, "radiusKm", 5.0, 0.2, 50.0);
        var radiusMeters = radiusKm * 1000.0;

        var startDate = OptionalDate(args, "date");
        var endDate = OptionalDate(args, "endDate");
        if (endDate.HasValue && !startDate.HasValue)
            throw new ToolArgumentException("Provide 'date' together with 'endDate'.");
        if (startDate.HasValue)
        {
            endDate ??= startDate;
            if (endDate.Value < startDate.Value)
                throw new ToolArgumentException("'endDate' must be on or after 'date'.");
        }

        var includePrivate = OptionalBool(args, "includePrivateBays", true);
        var includePublic = OptionalBool(args, "includePublicLots", true);
        var coveredOnly = OptionalBool(args, "coveredOnly", false);
        var evOnly = OptionalBool(args, "evOnly", false);
        var cctvOnly = OptionalBool(args, "cctvOnly", false);
        var accessibleOnly = OptionalBool(args, "accessibleOnly", false);
        var maxDailyRate = TryNumber(args, "maxDailyRate", out var maxRate) && maxRate > 0
            ? (decimal?)Math.Round((decimal)maxRate, 2)
            : null;

        var today = TodayInKl();
        var minBookingDate = CopilotAvailabilityHelper.GetMinimumBookingDate(today);
        var privateSameDayBlocked = startDate.HasValue && startDate.Value < minBookingDate;

        // Public lots do not expose covered/EV/CCTV/accessibility flags in the schema.
        var hasPrivateAmenityFilter = coveredOnly || evOnly || cctvOnly || accessibleOnly;
        if (hasPrivateAmenityFilter)
            includePublic = false;

        // 1. Resolve search anchors (explicit coords OR matching GTFS stations OR Nominatim geocode)
        var anchors = new List<(double Lat, double Lng, string Name, string? StopId)>();
        if (hasLat)
        {
            anchors.Add((explicitLat, explicitLng, "Target coordinates", null));
        }
        else if (!string.IsNullOrWhiteSpace(query))
        {
            var matchedStops = _gtfs.GetAllStops(null, query).Take(5).ToList();
            foreach (var stop in matchedStops)
            {
                anchors.Add((stop.Latitude, stop.Longitude, stop.StopName, stop.StopId));
            }

            if (anchors.Count == 0)
            {
                try
                {
                    var places = await _nominatim.SearchAsync(query, 2);
                    foreach (var p in places)
                    {
                        if (double.TryParse(p.Lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var pLat) &&
                            double.TryParse(p.Lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var pLng))
                        {
                            anchors.Add((pLat, pLng, string.IsNullOrWhiteSpace(p.PropertyName) ? query : p.PropertyName, null));
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogDebug(ex, "Nominatim fallback failed during searchParking for '{Query}'", query);
                }
            }
        }

        // 2. Load real DB data (bookable deduplicated private bays + active public lots)
        var windowEnd = ComputeBookingWindowEnd(today, endDate);
        var spotBundles = includePrivate
            ? await LoadBookableSpotBundlesAsync(today, userId, windowEnd, ct)
            : new List<SpotBundle>();
        var publicLots = includePublic
            ? await _publicParking.GetLotsAsync(includeAll: false, ct)
            : new List<PublicParkingLotDto>();

        var filteredBundles = spotBundles.Where(b =>
        {
            var s = b.Spot;
            if (coveredOnly && !s.IsCovered) return false;
            if (evOnly && !s.HasEvCharging) return false;
            if (cctvOnly && !s.HasCctv) return false;
            if (accessibleOnly && !s.IsHandicapAccessible) return false;
            if (maxDailyRate.HasValue && (!s.DailyRate.HasValue || s.DailyRate.Value > maxDailyRate.Value)) return false;
            return true;
        }).ToList();

        var filteredPublicLots = publicLots.Where(lot =>
        {
            if (maxDailyRate.HasValue)
            {
                var effectiveDayCost = lot.DailyCapRate ?? lot.FlatRatePerEntry ?? lot.HourlyRate;
                if (effectiveDayCost > maxDailyRate.Value) return false;
            }
            return true;
        }).ToList();

        // 3. Evaluate matches against query and/or anchors
        var matchedSpots = new List<MatchedPrivateSpot>();
        var spotNetworkDistances = new List<(SpotBundle Bundle, double DistMeters)>();

        foreach (var bundle in filteredBundles)
        {
            var prop = bundle.Spot.Property;
            var propLat = (double)prop.Latitude;
            var propLng = (double)prop.Longitude;

            double? distMeters = null;
            (double Lat, double Lng, string Name, string? StopId)? bestAnchor = null;
            if (anchors.Count > 0)
            {
                foreach (var anchor in anchors)
                {
                    var d = GtfsService.CalculateDistanceMeters(anchor.Lat, anchor.Lng, propLat, propLng);
                    if (!distMeters.HasValue || d < distMeters.Value)
                    {
                        distMeters = d;
                        bestAnchor = anchor;
                    }
                }
                spotNetworkDistances.Add((bundle, distMeters!.Value));
            }

            var textScore = !string.IsNullOrWhiteSpace(query) && !hasLat
                ? ParkingFuzzySearchService.EvaluateSpot(query, bundle.Spot).Score
                : 0;

            var geoMatch = distMeters.HasValue && distMeters.Value <= radiusMeters;
            var textMatch = !string.IsNullOrWhiteSpace(query) && !hasLat && textScore >= 60;
            var openBrowse = anchors.Count == 0 && string.IsNullOrWhiteSpace(query);

            if (!geoMatch && !textMatch && !openBrowse)
                continue;

            var resolvedStation = bestAnchor?.StopId != null
                ? bestAnchor.Value.Name
                : prop.Station?.StationName ?? string.Empty;
            var fallbackStationDistMeters = Math.Round((double)prop.DistanceToStation * 1000.0, 1);

            var availability = EvaluateSpotDateState(bundle, startDate, endDate, today);
            matchedSpots.Add(new MatchedPrivateSpot(
                bundle,
                distMeters ?? fallbackStationDistMeters,
                textScore,
                resolvedStation,
                bestAnchor?.StopId ?? string.Empty,
                availability.AvailableOnRequestedDate,
                availability.NextAvailableDate,
                availability.StatusReason));
        }

        var matchedLots = new List<MatchedPublicLot>();
        var lotNetworkDistances = new List<(PublicParkingLotDto Lot, double DistMeters)>();

        foreach (var lot in filteredPublicLots)
        {
            var lotLat = (double)lot.Latitude;
            var lotLng = (double)lot.Longitude;

            double? distMeters = null;
            (double Lat, double Lng, string Name, string? StopId)? bestAnchor = null;
            if (anchors.Count > 0)
            {
                foreach (var anchor in anchors)
                {
                    var d = GtfsService.CalculateDistanceMeters(anchor.Lat, anchor.Lng, lotLat, lotLng);
                    if (!distMeters.HasValue || d < distMeters.Value)
                    {
                        distMeters = d;
                        bestAnchor = anchor;
                    }
                }
                lotNetworkDistances.Add((lot, distMeters!.Value));
            }

            var textScore = !string.IsNullOrWhiteSpace(query) && !hasLat
                ? ScorePublicLotText(query, lot)
                : 0;

            var geoMatch = distMeters.HasValue && distMeters.Value <= radiusMeters;
            var textMatch = !string.IsNullOrWhiteSpace(query) && !hasLat && textScore >= 60;
            var openBrowse = anchors.Count == 0 && string.IsNullOrWhiteSpace(query);

            if (!geoMatch && !textMatch && !openBrowse)
                continue;

            matchedLots.Add(new MatchedPublicLot(
                lot,
                distMeters ?? Math.Round((lot.DistanceToStation ?? 0) * 1.0, 1),
                textScore,
                bestAnchor?.Name ?? lot.NearestStationName ?? string.Empty));
        }

        matchedSpots = matchedSpots
            .OrderByDescending(m => m.AvailableOnRequestedDate ?? true)
            .ThenByDescending(m => m.TextScore >= 75 ? m.TextScore : 0)
            .ThenBy(m => m.DistanceMeters)
            .ToList();

        matchedLots = matchedLots
            .OrderByDescending(m => m.Lot.IsCurrentlyOpen && m.Lot.AvailableSpaces > 0)
            .ThenByDescending(m => m.TextScore >= 75 ? m.TextScore : 0)
            .ThenBy(m => m.DistanceMeters)
            .ToList();

        var foundNearby = matchedSpots.Count > 0 || matchedLots.Count > 0;

        // Critical card hygiene: when zero bays/lots match inside the requested area/query, emit ZERO cards
        // and return closestInNetwork in JSON text only so the model answers honestly.
        if (!foundNearby)
        {
            var closestArr = new JsonArray();
            var closestPrivate = spotNetworkDistances
                .OrderBy(x => x.DistMeters)
                .Take(2)
                .Select(x => new
                {
                    DistKm = Math.Round(x.DistMeters / 1000.0, 1),
                    Node = (JsonNode)new JsonObject
                    {
                        ["type"] = "private_bay",
                        ["bayId"] = x.Bundle.Spot.ParkingSpotId,
                        ["label"] = x.Bundle.Spot.ParkingLabel ?? $"Bay {x.Bundle.Spot.ParkingSpotId}",
                        ["property"] = x.Bundle.Spot.Property.PropertyName,
                        ["station"] = x.Bundle.Spot.Property.Station?.StationName ?? string.Empty,
                        ["distanceKm"] = Math.Round(x.DistMeters / 1000.0, 1),
                        ["dailyRateRm"] = x.Bundle.Spot.DailyRate,
                        ["monthlyRateRm"] = x.Bundle.Spot.MonthlyRate,
                        ["nextAvailableDate"] = EvaluateSpotDateState(x.Bundle, startDate, endDate, today)
                            .NextAvailableDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    }
                });

            var closestPublic = lotNetworkDistances
                .OrderBy(x => x.DistMeters)
                .Take(2)
                .Select(x => new
                {
                    DistKm = Math.Round(x.DistMeters / 1000.0, 1),
                    Node = (JsonNode)new JsonObject
                    {
                        ["type"] = "public_drive_in_lot",
                        ["publicParkingLotId"] = x.Lot.PublicParkingLotId,
                        ["lotName"] = x.Lot.LotName,
                        ["address"] = x.Lot.Address,
                        ["distanceKm"] = Math.Round(x.DistMeters / 1000.0, 1),
                        ["availableSpaces"] = x.Lot.AvailableSpaces,
                        ["totalCapacity"] = x.Lot.TotalCapacity,
                        ["isCurrentlyOpen"] = x.Lot.IsCurrentlyOpen,
                        ["hourlyRateRm"] = x.Lot.HourlyRate,
                        ["dailyCapRateRm"] = x.Lot.DailyCapRate,
                    }
                });

            foreach (var item in closestPrivate.Concat(closestPublic).OrderBy(x => x.DistKm).Take(3))
            {
                closestArr.Add(item.Node);
            }

            var anchorNames = anchors.Count > 0
                ? string.Join(", ", anchors.Select(a => a.Name).Distinct(StringComparer.OrdinalIgnoreCase).Take(3))
                : query ?? "the requested location";

            return new JsonObject
            {
                ["foundNearby"] = false,
                ["searchedArea"] = anchorNames,
                ["radiusKm"] = radiusKm,
                ["requestedDate"] = startDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["earliestPrivateBookingDate"] = minBookingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["privateBookingAllowedForRequestedDate"] = !privateSameDayBlocked,
                ["privateBays"] = new JsonArray(),
                ["publicLots"] = new JsonArray(),
                ["closestInNetwork"] = closestArr,
                ["hint"] = $"No ParkJom private bays or public lots found within {radiusKm:0.#} km of '{anchorNames}'. Do NOT pretend closestInNetwork options are at '{anchorNames}'; honestly state nothing is available there yet and quote distanceKm from closestInNetwork if helpful.",
            };
        }

        // Emit cards only for verified local/query matches (preferring open public drive-in lots when date == today)
        var emittedLots = new JsonArray();
        var emittedBays = new JsonArray();

        if (privateSameDayBlocked)
        {
            foreach (var m in matchedLots)
            {
                if (Cards.Count >= MaxParkingCardsPerTurn) break;
                emittedLots.Add(EmitPublicLotCard(m.Lot, m.DistanceMeters, m.StationLabel));
            }
            foreach (var m in matchedSpots)
            {
                if (Cards.Count >= MaxParkingCardsPerTurn) break;
                emittedBays.Add(await EmitPrivateBayCardAsync(
                    m.Bundle,
                    m.DistanceMeters,
                    m.StationName,
                    m.StopId,
                    lineName: null,
                    m.AvailableOnRequestedDate,
                    m.NextAvailableDate,
                    m.StatusReason,
                    ct));
            }
        }
        else
        {
            foreach (var m in matchedSpots)
            {
                if (Cards.Count >= MaxParkingCardsPerTurn) break;
                emittedBays.Add(await EmitPrivateBayCardAsync(
                    m.Bundle,
                    m.DistanceMeters,
                    m.StationName,
                    m.StopId,
                    lineName: null,
                    m.AvailableOnRequestedDate,
                    m.NextAvailableDate,
                    m.StatusReason,
                    ct));
            }
            foreach (var m in matchedLots)
            {
                if (Cards.Count >= MaxParkingCardsPerTurn) break;
                emittedLots.Add(EmitPublicLotCard(m.Lot, m.DistanceMeters, m.StationLabel));
            }
        }

        var response = new JsonObject
        {
            ["foundNearby"] = true,
            ["searchedArea"] = anchors.Count > 0 ? anchors[0].Name : query ?? "all",
            ["radiusKm"] = radiusKm,
            ["requestedDate"] = startDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["earliestPrivateBookingDate"] = minBookingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["privateBookingAllowedForRequestedDate"] = !privateSameDayBlocked,
            ["privateBays"] = emittedBays,
            ["publicLots"] = emittedLots,
        };

        if (privateSameDayBlocked)
        {
            response["note"] = $"Private bays require advance booking starting tomorrow ({minBookingDate:yyyy-MM-dd}) and cannot be booked for today ({today:yyyy-MM-dd}). Only open public drive-in lots can be used today.";
        }

        return response;
    }

    private async Task<JsonNode> CheckSpotAvailabilityAsync(JsonObject args, int userId, CancellationToken ct)
    {
        var spotId = (int)RequireNumber(args, "parkingSpotId", 1, int.MaxValue);
        var startDate = OptionalDate(args, "startDate");
        var endDate = OptionalDate(args, "endDate");
        if (endDate.HasValue && !startDate.HasValue)
            throw new ToolArgumentException("Provide 'startDate' together with 'endDate'.");

        var spot = await _db.ParkingSpots
            .AsNoTracking()
            .Include(s => s.Property)
            .Include(s => s.Owner)
            .Include(s => s.VerificationRequests.Where(vr => vr.IsCurrent))
            .Include(s => s.ParkingAvailabilities)
            .FirstOrDefaultAsync(s => s.ParkingSpotId == spotId, ct);

        if (spot == null)
            return Error($"Parking bay {spotId} was not found.");

        if (spot.OwnerId == userId)
            return Error("You cannot book your own parking bay.");

        var isApproved = spot.IsPublished
            && spot.AvailabilityStatus == AvailabilityStatus.Available
            && spot.Owner?.AccountStatus != "Suspended"
            && spot.Owner?.AccountStatus != "Deactivated"
            && spot.VerificationRequests.Any(vr => vr.IsCurrent && vr.VerificationStatus == VerificationStatus.Approved);

        var today = TodayInKl();
        var minDate = CopilotAvailabilityHelper.GetMinimumBookingDate(today);

        if (!isApproved)
        {
            return new JsonObject
            {
                ["parkingSpotId"] = spot.ParkingSpotId,
                ["property"] = spot.Property.PropertyName,
                ["label"] = spot.ParkingLabel ?? $"Bay {spot.ParkingSpotId}",
                ["isBookable"] = false,
                ["isAvailable"] = false,
                ["reason"] = "This parking bay is currently paused, unpublished, or unverified.",
            };
        }

        var checkStart = startDate ?? minDate;
        var checkEnd = endDate ?? checkStart;
        var windowEnd = ComputeBookingWindowEnd(today, checkEnd);
        var startBoundary = (checkStart < minDate ? checkStart : minDate).ToDateTime(TimeOnly.MinValue);
        var endBoundary = windowEnd.ToDateTime(TimeOnly.MinValue);

        var blockingBookings = await _db.Bookings
            .AsNoTracking()
            .Where(b =>
                b.ParkingSpotId == spotId &&
                (b.BookingStatus == BookingStatus.Confirmed || b.BookingStatus == BookingStatus.Active) &&
                b.StartDate < endBoundary &&
                b.EndDate > startBoundary)
            .ToListAsync(ct);

        var evaluation = CopilotAvailabilityHelper.EvaluateDateRange(spot, blockingBookings, checkStart, checkEnd, today);
        var nextDatesArr = new JsonArray(
            evaluation.NextAvailableDates
                .Select(d => (JsonNode?)JsonValue.Create(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
                .ToArray());

        var result = new JsonObject
        {
            ["parkingSpotId"] = spot.ParkingSpotId,
            ["label"] = spot.ParkingLabel ?? $"Bay {spot.ParkingSpotId}",
            ["property"] = spot.Property.PropertyName,
            ["isBookable"] = true,
            ["minimumBookingDate"] = minDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["dailyRate"] = spot.DailyRate,
            ["monthlyRate"] = spot.MonthlyRate,
            ["isCovered"] = spot.IsCovered,
            ["hasEvCharging"] = spot.HasEvCharging,
            ["evChargingSpeed"] = spot.EvChargingSpeed,
            ["hasCctv"] = spot.HasCctv,
            ["isHandicapAccessible"] = spot.IsHandicapAccessible,
            ["nextAvailableDates"] = nextDatesArr,
        };

        if (startDate.HasValue)
        {
            result["checkedStartDate"] = checkStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            result["checkedEndDate"] = checkEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            result["isAvailable"] = evaluation.IsAvailable;
            result["reason"] = evaluation.ErrorReason;
            result["unavailableDates"] = new JsonArray(
                evaluation.UnavailableDates
                    .Select(d => (JsonNode?)JsonValue.Create(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
                    .ToArray());
            result["days"] = evaluation.AvailableDayCount;
            result["rateType"] = evaluation.IsAvailable ? evaluation.RateType.ToString() : null;
            result["estimatedSubtotal"] = evaluation.EstimatedSubtotal;
        }
        else
        {
            result["isAvailable"] = evaluation.NextAvailableDates.Count > 0;
            result["reason"] = evaluation.NextAvailableDates.Count > 0
                ? null
                : "No open dates found in the next 30 days.";
        }

        return result;
    }

    private async Task<JsonNode> GetMyBookingsAsync(int userId, CancellationToken ct)
    {
        var todayStart = TodayInKl().ToDateTime(TimeOnly.MinValue);

        var bookings = await _db.Bookings
            .AsNoTracking()
            .Include(b => b.ParkingSpot)
                .ThenInclude(ps => ps.Property)
            .Include(b => b.Vehicle)
            .Where(b =>
                b.RenterId == userId &&
                (b.BookingStatus == BookingStatus.Confirmed ||
                 b.BookingStatus == BookingStatus.Active ||
                 b.BookingStatus == BookingStatus.Pending))
            .OrderByDescending(b => b.BookingStatus == BookingStatus.Active)
            .ThenByDescending(b => b.EndDate >= todayStart)
            .ThenBy(b => b.StartDate)
            .Take(10)
            .ToListAsync(ct);

        var arr = new JsonArray();
        foreach (var b in bookings)
        {
            var startDate = DateOnly.FromDateTime(b.StartDate);
            var endExclusive = CopilotAvailabilityHelper.GetBookingEndExclusiveDate(b.EndDate);
            var inclusiveEnd = endExclusive > startDate ? endExclusive.AddDays(-1) : startDate;

            arr.Add(new JsonObject
            {
                ["bookingId"] = b.BookingId,
                ["bookingReference"] = b.BookingReference,
                ["status"] = b.BookingStatus.ToString(),
                ["parkingSpotId"] = b.ParkingSpotId,
                ["parkingLabel"] = b.ParkingSpot?.ParkingLabel ?? $"Bay {b.ParkingSpotId}",
                ["propertyName"] = b.ParkingSpot?.Property?.PropertyName ?? string.Empty,
                ["startDate"] = startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["endDate"] = inclusiveEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["bookedDays"] = b.BookedDays,
                ["totalAmountRm"] = b.TotalAmount,
                ["vehiclePlate"] = b.Vehicle?.NumberPlate ?? string.Empty,
            });
        }

        return new JsonObject
        {
            ["count"] = arr.Count,
            ["bookings"] = arr,
        };
    }

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

    private async Task<JsonNode> FindStationsNearAsync(JsonObject args, int userId, CancellationToken ct)
    {
        var lat = RequireNumber(args, "lat", -90, 90);
        var lng = RequireNumber(args, "lng", -180, 180);
        var radius = OptionalNumber(args, "radiusMeters", 3500, 100, 10000);
        var limit = (int)OptionalNumber(args, "limit", 4, 1, 6);
        var requestedDate = OptionalDate(args, "date");

        var stations = _gtfs.GetNearbyStations(lat, lng, radius, limit);
        if (stations.Count == 0)
            return Error("No stations within radius. Try radiusMeters=6000.");

        return await BuildStationsWithParkingAsync(stations, DefaultStationParkingRadiusMeters, requestedDate, userId, ct);
    }

    private async Task<JsonNode> SearchStationsAsync(JsonObject args, int userId, CancellationToken ct)
    {
        var name = OptionalString(args, "name", 100);
        var line = OptionalString(args, "line", 50);
        var requestedDate = OptionalDate(args, "date");
        if (name == null && line == null) throw new ToolArgumentException("Provide 'name' or 'line'.");

        var stations = _gtfs.GetAllStops(line, name).Take(5).ToList();
        if (stations.Count == 0)
            return Error("No matching stations.");

        return await BuildStationsWithParkingAsync(stations, DefaultStationParkingRadiusMeters, requestedDate, userId, ct);
    }

    private async Task<JsonNode> GetParkingNearStationAsync(JsonObject args, int userId, CancellationToken ct)
    {
        var stopId = RequireString(args, "stopId", 50);
        var radius = OptionalNumber(args, "radiusMeters", DefaultStationParkingRadiusMeters, 100, 3000);
        var requestedDate = OptionalDate(args, "date");

        var stop = _gtfs.GetStopById(stopId);
        if (stop == null) return Error($"Unknown stopId '{stopId}'.");

        var today = TodayInKl();
        var windowEnd = ComputeBookingWindowEnd(today, requestedDate);
        var spotBundles = await LoadBookableSpotBundlesAsync(today, userId, windowEnd, ct);
        var publicLots = await _publicParking.GetLotsAsync(includeAll: false, ct);

        var nearBays = await EmitStationAreaBaysAsync(stop, radius, requestedDate, today, spotBundles, ct);
        var nearLots = EmitStationAreaPublicLots(stop, radius, publicLots);

        return new JsonObject
        {
            ["station"] = stop.StopName,
            ["stopId"] = stop.StopId,
            ["bays"] = nearBays,
            ["publicLots"] = nearLots,
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

        return new JsonObject
        {
            ["station"] = schedule.StopName,
            ["line"] = schedule.RouteName,
            ["open"] = schedule.IsOperating,
            ["everyMin"] = schedule.FrequencyMinutes,
            ["nextTrains"] = slimDepartures,
        };
    }

    private async Task<JsonNode> ProposeBookingAsync(JsonObject args, int userId, CancellationToken ct)
    {
        var spotId = (int)RequireNumber(args, "parkingSpotId", 1, int.MaxValue);
        var start = RequireDate(args, "startDate");
        var end = RequireDate(args, "endDate");
        var today = TodayInKl();
        var minDate = CopilotAvailabilityHelper.GetMinimumBookingDate(today);

        if (start < minDate)
        {
            return Error($"Private bay bookings must start tomorrow ({minDate:yyyy-MM-dd}) or later in Malaysia time. For today ({today:yyyy-MM-dd}), use a public drive-in parking lot via searchParking.");
        }
        if (end < start) return Error("endDate must be >= startDate.");
        var days = end.DayNumber - start.DayNumber + 1;
        if (days > CopilotAvailabilityHelper.MaxBookingDays)
            return Error($"Max {CopilotAvailabilityHelper.MaxBookingDays} days.");

        var spot = await _db.ParkingSpots
            .AsNoTracking()
            .Include(s => s.Property)
            .Include(s => s.Owner)
            .Include(s => s.VerificationRequests.Where(vr => vr.IsCurrent))
            .Include(s => s.ParkingAvailabilities)
            .FirstOrDefaultAsync(s => s.ParkingSpotId == spotId, ct);

        if (spot == null ||
            !spot.IsPublished ||
            spot.AvailabilityStatus != AvailabilityStatus.Available ||
            spot.Owner?.AccountStatus == "Suspended" ||
            spot.Owner?.AccountStatus == "Deactivated" ||
            !spot.VerificationRequests.Any(vr => vr.IsCurrent && vr.VerificationStatus == VerificationStatus.Approved))
        {
            return Error($"Bay {spotId} is not available for booking.");
        }

        if (spot.OwnerId == userId)
            return Error("You cannot book your own parking bay.");

        var windowEnd = ComputeBookingWindowEnd(today, end);
        var blockingBookings = await _db.Bookings
            .AsNoTracking()
            .Where(b =>
                b.ParkingSpotId == spotId &&
                (b.BookingStatus == BookingStatus.Confirmed || b.BookingStatus == BookingStatus.Active) &&
                b.StartDate < windowEnd.ToDateTime(TimeOnly.MinValue) &&
                b.EndDate > minDate.ToDateTime(TimeOnly.MinValue))
            .ToListAsync(ct);

        var availability = CopilotAvailabilityHelper.EvaluateDateRange(spot, blockingBookings, start, end, today);
        if (!availability.IsAvailable)
        {
            return new JsonObject
            {
                ["error"] = availability.ErrorReason ?? $"Bay {spotId} is unavailable for the requested dates.",
                ["unavailableDates"] = new JsonArray(
                    availability.UnavailableDates
                        .Select(d => (JsonNode?)JsonValue.Create(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
                        .ToArray()),
                ["nextAvailableDates"] = new JsonArray(
                    availability.NextAvailableDates
                        .Select(d => (JsonNode?)JsonValue.Create(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
                        .ToArray()),
            };
        }

        var card = new JsonObject
        {
            ["parkingSpotId"] = spot.ParkingSpotId,
            ["parkingLabel"] = spot.ParkingLabel ?? $"Bay {spot.ParkingSpotId}",
            ["propertyName"] = spot.Property.PropertyName,
            ["address"] = spot.Property.Address,
            ["startDate"] = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["endDate"] = end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["days"] = availability.AvailableDayCount,
            ["dailyRate"] = spot.DailyRate ?? availability.RatePerDay,
            ["estimatedSubtotal"] = availability.EstimatedSubtotal,
        };
        AddCard("bookingProposal", $"proposal:{spotId}:{start}:{end}", card);
        _context.Append($"Proposed bay#{spotId} {start:yyyy-MM-dd}..{end:yyyy-MM-dd}. ");

        return new JsonObject
        {
            ["status"] = "proposal_card_shown",
            ["bayId"] = spot.ParkingSpotId,
            ["property"] = spot.Property.PropertyName,
            ["days"] = availability.AvailableDayCount,
            ["rateType"] = availability.RateType.ToString(),
            ["estRm"] = availability.EstimatedSubtotal,
        };
    }

    private async Task<JsonNode> PredictParkingCongestionAsync(JsonObject args, CancellationToken ct)
    {
        var stopId = OptionalString(args, "stopId", 50);
        int? spotId = TryNumber(args, "parkingSpotId", out var n) && n >= 1 ? (int)n : null;

        if (spotId == null && string.IsNullOrWhiteSpace(stopId))
            throw new ToolArgumentException("Provide 'stopId' or 'parkingSpotId'.");

        var forecast = spotId.HasValue
            ? await _predictor.PredictSpotCongestionAsync(spotId.Value, null, ct)
            : await _predictor.PredictStationCongestionAsync(stopId!, null, ct);

        if (forecast == null)
            return Error("Could not generate congestion forecast for the requested location.");

        var factorsArr = new JsonArray(forecast.RiskFactors.Select(f => (JsonNode?)new JsonObject
        {
            ["label"] = f.Label,
            ["impact"] = f.Impact,
            ["direction"] = f.Direction,
        }).ToArray());

        return new JsonObject
        {
            ["target"] = forecast.TargetName,
            ["stopId"] = forecast.StopId,
            ["predictedOccupancyPct"] = forecast.PredictedOccupancyPercent,
            ["probabilityFull"] = forecast.ProbabilityFull,
            ["riskLevel"] = forecast.RiskLevel,
            ["estimatedFullBy"] = forecast.EstimatedFullBy,
            ["recommendedArriveBefore"] = forecast.RecommendedArriveBefore,
            ["recommendation"] = forecast.Recommendation,
            ["weather"] = $"{forecast.Weather.Condition} ({forecast.Weather.RainProbability:0}% rain)",
            ["factors"] = factorsArr,
        };
    }

    // ───────────────────────── Helpers ─────────────────────────

    private async Task<JsonObject> BuildStationsWithParkingAsync(
        List<GtfsStopDto> stations,
        double radiusMeters,
        DateOnly? requestedDate,
        int userId,
        CancellationToken ct)
    {
        var today = TodayInKl();
        var minDate = CopilotAvailabilityHelper.GetMinimumBookingDate(today);
        var windowEnd = ComputeBookingWindowEnd(today, requestedDate);

        var spotBundles = await LoadBookableSpotBundlesAsync(today, userId, windowEnd, ct);
        var publicLots = await _publicParking.GetLotsAsync(includeAll: false, ct);

        var stationArr = new JsonArray();
        var allBays = new JsonArray();
        var allPublicLots = new JsonArray();
        var shownStations = new List<string>();

        foreach (var s in stations)
        {
            stationArr.Add(new JsonObject
            {
                ["stopId"] = s.StopId,
                ["name"] = s.StopName,
                ["line"] = s.RouteName,
                ["category"] = s.Category,
                ["distM"] = s.DistanceMeters,
            });
            shownStations.Add($"{s.StopName}[{s.StopId}]");

            if (Cards.Count < MaxParkingCardsPerTurn)
            {
                foreach (var lotNode in EmitStationAreaPublicLots(s, radiusMeters, publicLots))
                    allPublicLots.Add(lotNode?.DeepClone());
            }

            if (Cards.Count < MaxParkingCardsPerTurn)
            {
                foreach (var bayNode in await EmitStationAreaBaysAsync(s, radiusMeters, requestedDate, today, spotBundles, ct))
                    allBays.Add(bayNode?.DeepClone());
            }
        }

        if (shownStations.Count > 0)
            _context.Append($"Stations: {string.Join(", ", shownStations)}. ");

        var hasAny = allBays.Count > 0 || allPublicLots.Count > 0;
        return new JsonObject
        {
            ["stations"] = stationArr,
            ["availableBays"] = allBays,
            ["publicLots"] = allPublicLots,
            ["earliestPrivateBookingDate"] = minDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["hint"] = hasAny
                ? "Nearby private bays and public drive-in lots are already included above; do not call getParkingNearStation again for these stations."
                : $"No ParkJom private bays or public drive-in lots within {radiusMeters / 1000.0:0.#} km of these stations. You can call searchParking to check closest options across the network.",
        };
    }

    private async Task<JsonArray> EmitStationAreaBaysAsync(
        GtfsStopDto stop,
        double radiusMeters,
        DateOnly? requestedDate,
        DateOnly today,
        List<SpotBundle> allBundles,
        CancellationToken ct)
    {
        var nearby = allBundles
            .Select(b => (
                Bundle: b,
                Dist: Math.Round(GtfsService.CalculateDistanceMeters(stop.Latitude, stop.Longitude, (double)b.Spot.Property.Latitude, (double)b.Spot.Property.Longitude), 1)))
            .Where(x => x.Dist <= radiusMeters)
            .OrderBy(x => x.Dist)
            .ToList();

        var slimBays = new JsonArray();
        foreach (var item in nearby)
        {
            if (Cards.Count >= MaxParkingCardsPerTurn) break;
            var availability = EvaluateSpotDateState(item.Bundle, requestedDate, requestedDate, today);
            slimBays.Add(await EmitPrivateBayCardAsync(
                item.Bundle,
                item.Dist,
                stop.StopName,
                stop.StopId,
                stop.RouteName,
                availability.AvailableOnRequestedDate,
                availability.NextAvailableDate,
                availability.StatusReason,
                ct));
        }

        return slimBays;
    }

    private JsonArray EmitStationAreaPublicLots(GtfsStopDto stop, double radiusMeters, List<PublicParkingLotDto> allLots)
    {
        var nearby = allLots
            .Select(lot => (
                Lot: lot,
                Dist: Math.Round(GtfsService.CalculateDistanceMeters(stop.Latitude, stop.Longitude, (double)lot.Latitude, (double)lot.Longitude), 1)))
            .Where(x => x.Dist <= radiusMeters)
            .OrderByDescending(x => x.Lot.IsCurrentlyOpen && x.Lot.AvailableSpaces > 0)
            .ThenBy(x => x.Dist)
            .ToList();

        var slimLots = new JsonArray();
        foreach (var item in nearby)
        {
            if (Cards.Count >= MaxParkingCardsPerTurn) break;
            slimLots.Add(EmitPublicLotCard(item.Lot, item.Dist, stop.StopName));
        }

        return slimLots;
    }

    private async Task<JsonObject> EmitPrivateBayCardAsync(
        SpotBundle bundle,
        double distanceMeters,
        string stationName,
        string stopId,
        string? lineName,
        bool? availableOnRequestedDate,
        DateOnly? nextAvailableDate,
        string? availabilityNote,
        CancellationToken ct)
    {
        var spot = bundle.Spot;
        var prop = spot.Property;
        var label = spot.ParkingLabel ?? $"Bay {spot.ParkingSpotId}";
        var resolvedStation = !string.IsNullOrWhiteSpace(stationName) ? stationName : prop.Station?.StationName ?? string.Empty;
        var resolvedDistMeters = Math.Round(distanceMeters, 1);
        var walkMinutes = (int)Math.Ceiling(resolvedDistMeters / 80.0);

        ParkingForecastResponseDto? forecast = null;
        try
        {
            forecast = await _predictor.PredictSpotCongestionAsync(spot.ParkingSpotId, null, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Congestion forecast unavailable for spot {SpotId}", spot.ParkingSpotId);
        }

        var nextDateIso = nextAvailableDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        AddCard("parking", $"parking:{spot.ParkingSpotId}", new JsonObject
        {
            ["parkingSpotId"] = spot.ParkingSpotId,
            ["parkingLabel"] = label,
            ["propertyName"] = prop.PropertyName,
            ["address"] = prop.Address,
            ["distanceToStationMeters"] = resolvedDistMeters,
            ["walkingTimeMinutes"] = walkMinutes,
            ["dailyRate"] = spot.DailyRate,
            ["monthlyRate"] = spot.MonthlyRate,
            ["availabilityStatus"] = spot.AvailabilityStatus.ToString(),
            ["hasSmartBollard"] = spot.IoTDevice != null,
            ["stationName"] = resolvedStation,
            ["stopId"] = stopId,
            ["lineName"] = lineName,
            ["primaryImageUrl"] = bundle.PrimaryImageUrl,
            ["isCovered"] = spot.IsCovered,
            ["hasEvCharging"] = spot.HasEvCharging,
            ["hasCctv"] = spot.HasCctv,
            ["isHandicapAccessible"] = spot.IsHandicapAccessible,
            ["availableOnRequestedDate"] = availableOnRequestedDate,
            ["nextAvailableDate"] = nextDateIso,
            ["predictedOccupancyPercent"] = forecast?.PredictedOccupancyPercent,
            ["probabilityFull"] = forecast?.ProbabilityFull,
            ["riskLevel"] = forecast?.RiskLevel,
            ["recommendedArriveBefore"] = forecast?.RecommendedArriveBefore,
        });

        _context.Append($"Bay id={spot.ParkingSpotId} '{label}' ({prop.PropertyName}, RM{spot.DailyRate:0.##}/d, next={nextDateIso ?? "-"}). ");

        return new JsonObject
        {
            ["bayId"] = spot.ParkingSpotId,
            ["label"] = label,
            ["property"] = prop.PropertyName,
            ["station"] = resolvedStation,
            ["stopId"] = stopId,
            ["distM"] = resolvedDistMeters,
            ["walkMin"] = walkMinutes,
            ["rmDay"] = spot.DailyRate,
            ["rmMonth"] = spot.MonthlyRate,
            ["isCovered"] = spot.IsCovered,
            ["hasEvCharging"] = spot.HasEvCharging,
            ["hasCctv"] = spot.HasCctv,
            ["isHandicapAccessible"] = spot.IsHandicapAccessible,
            ["availableOnRequestedDate"] = availableOnRequestedDate,
            ["nextAvailableDate"] = nextDateIso,
            ["availabilityNote"] = availabilityNote,
            ["predictedOccupancyPct"] = forecast?.PredictedOccupancyPercent,
            ["riskLevel"] = forecast?.RiskLevel,
        };
    }

    private JsonObject EmitPublicLotCard(PublicParkingLotDto lot, double distanceMeters, string stationLabel)
    {
        var resolvedDist = Math.Round(distanceMeters, 1);
        var walkMinutes = (int)Math.Ceiling(resolvedDist / 80.0);
        var resolvedStation = !string.IsNullOrWhiteSpace(stationLabel)
            ? stationLabel
            : lot.NearestStationName ?? string.Empty;

        AddCard("publicParking", $"public:{lot.PublicParkingLotId}", new JsonObject
        {
            ["publicParkingLotId"] = lot.PublicParkingLotId,
            ["lotName"] = lot.LotName,
            ["address"] = lot.Address,
            ["latitude"] = lot.Latitude,
            ["longitude"] = lot.Longitude,
            ["distanceMeters"] = resolvedDist,
            ["walkingTimeMinutes"] = walkMinutes,
            ["availableSpaces"] = lot.AvailableSpaces,
            ["totalCapacity"] = lot.TotalCapacity,
            ["isCurrentlyOpen"] = lot.IsCurrentlyOpen,
            ["openingTime"] = lot.OperatingType == PublicParkingOperatingType.Unlimited24Hours ? "24 hours" : lot.OpeningTime,
            ["closingTime"] = lot.OperatingType == PublicParkingOperatingType.Unlimited24Hours ? null : lot.ClosingTime,
            ["hourlyRate"] = lot.HourlyRate,
            ["dailyCapRate"] = lot.DailyCapRate,
            ["flatRatePerEntry"] = lot.FlatRatePerEntry,
            ["nearestStationName"] = resolvedStation,
            ["facilityPhotoUrl"] = lot.FacilityPhotoUrl,
        });

        _context.Append($"PublicLot id={lot.PublicParkingLotId} '{lot.LotName}' ({lot.AvailableSpaces}/{lot.TotalCapacity} free, open={lot.IsCurrentlyOpen}). ");

        return new JsonObject
        {
            ["publicParkingLotId"] = lot.PublicParkingLotId,
            ["lotName"] = lot.LotName,
            ["address"] = lot.Address,
            ["distM"] = resolvedDist,
            ["walkMin"] = walkMinutes,
            ["availableSpaces"] = lot.AvailableSpaces,
            ["totalCapacity"] = lot.TotalCapacity,
            ["isCurrentlyOpen"] = lot.IsCurrentlyOpen,
            ["hourlyRateRm"] = lot.HourlyRate,
            ["dailyCapRateRm"] = lot.DailyCapRate,
            ["flatRatePerEntryRm"] = lot.FlatRatePerEntry,
            ["usableTodayDriveIn"] = lot.IsCurrentlyOpen && lot.AvailableSpaces > 0,
        };
    }

    private async Task<List<SpotBundle>> LoadBookableSpotBundlesAsync(
        DateOnly todayInKl,
        int userId,
        DateOnly windowEndExclusive,
        CancellationToken ct)
    {
        var minDate = CopilotAvailabilityHelper.GetMinimumBookingDate(todayInKl);

        var rawSpots = await _db.ParkingSpots
            .AsNoTracking()
            .Include(ps => ps.Property)
                .ThenInclude(p => p.Station)
            .Include(ps => ps.Owner)
            .Include(ps => ps.ParkingSpotImages)
                .ThenInclude(psi => psi.MediaFile)
            .Include(ps => ps.IoTDevice)
            .Include(ps => ps.ParkingAvailabilities)
            .Where(ps =>
                ps.IsPublished &&
                ps.AvailabilityStatus == AvailabilityStatus.Available &&
                ps.OwnerId != userId &&
                ps.Owner.AccountStatus != "Suspended" &&
                ps.Owner.AccountStatus != "Deactivated" &&
                (ps.DailyRate.HasValue || ps.MonthlyRate.HasValue) &&
                ps.VerificationRequests.Any(vr => vr.IsCurrent && vr.VerificationStatus == VerificationStatus.Approved))
            .ToListAsync(ct);

        // Deduplicate duplicate bay rows at the same property + label (keep highest quality record).
        var deduped = rawSpots
            .GroupBy(ps =>
            {
                var compactLabel = ParkingFuzzySearchService.NormalizeCompact(ps.ParkingLabel);
                var labelKey = string.IsNullOrEmpty(compactLabel) ? $"spot:{ps.ParkingSpotId}" : compactLabel;
                return (ps.PropertyId, labelKey);
            })
            .Select(group => group
                .OrderByDescending(ps => ps.DailyRate.HasValue)
                .ThenByDescending(ps => GetPrimaryImageUrl(ps) != null)
                .ThenByDescending(ps => ps.ParkingAvailabilities.Count > 0)
                .ThenBy(ps => ps.ParkingSpotId)
                .First())
            .ToList();

        if (deduped.Count == 0)
            return new List<SpotBundle>();

        var spotIds = deduped.Select(s => s.ParkingSpotId).ToList();
        var startBoundary = minDate.ToDateTime(TimeOnly.MinValue);
        var endBoundary = windowEndExclusive.ToDateTime(TimeOnly.MinValue);

        var activeBookings = await _db.Bookings
            .AsNoTracking()
            .Where(b =>
                spotIds.Contains(b.ParkingSpotId) &&
                (b.BookingStatus == BookingStatus.Confirmed || b.BookingStatus == BookingStatus.Active) &&
                b.StartDate < endBoundary &&
                b.EndDate > startBoundary)
            .ToListAsync(ct);

        var bookingsBySpot = activeBookings
            .GroupBy(b => b.ParkingSpotId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return deduped
            .Select(spot => new SpotBundle(
                spot,
                GetPrimaryImageUrl(spot),
                bookingsBySpot.TryGetValue(spot.ParkingSpotId, out var list) ? list : new List<Booking>()))
            .ToList();
    }

    private static DateOnly ComputeBookingWindowEnd(DateOnly today, DateOnly? requestedEnd)
    {
        var defaultEnd = CopilotAvailabilityHelper.GetMinimumBookingDate(today).AddDays(CopilotAvailabilityHelper.DefaultLookaheadDays + 1);
        if (!requestedEnd.HasValue) return defaultEnd;
        var requestedExclusive = requestedEnd.Value.AddDays(1);
        return requestedExclusive > defaultEnd ? requestedExclusive : defaultEnd;
    }

    private static (bool? AvailableOnRequestedDate, DateOnly? NextAvailableDate, string? StatusReason) EvaluateSpotDateState(
        SpotBundle bundle,
        DateOnly? requestedStart,
        DateOnly? requestedEnd,
        DateOnly today)
    {
        var minDate = CopilotAvailabilityHelper.GetMinimumBookingDate(today);
        if (!requestedStart.HasValue)
        {
            var probe = CopilotAvailabilityHelper.EvaluateDateRange(bundle.Spot, bundle.BlockingBookings, minDate, minDate, today);
            DateOnly? firstOpen = probe.NextAvailableDates.Count > 0 ? probe.NextAvailableDates[0] : null;
            return (null, firstOpen, null);
        }

        var eval = CopilotAvailabilityHelper.EvaluateDateRange(
            bundle.Spot,
            bundle.BlockingBookings,
            requestedStart.Value,
            requestedEnd ?? requestedStart.Value,
            today);

        DateOnly? next = eval.NextAvailableDates.Count > 0 ? eval.NextAvailableDates[0] : null;
        return (eval.IsAvailable, next, eval.ErrorReason);
    }

    private static int ScorePublicLotText(string rawQuery, PublicParkingLotDto lot)
    {
        var queryTrim = rawQuery.Trim();
        if (string.IsNullOrEmpty(queryTrim)) return 100;

        var queryCompact = ParkingFuzzySearchService.NormalizeCompact(queryTrim);
        var nameCompact = ParkingFuzzySearchService.NormalizeCompact(lot.LotName);
        var stationCompact = ParkingFuzzySearchService.NormalizeCompact(lot.NearestStationName);
        var addrCompact = ParkingFuzzySearchService.NormalizeCompact(lot.Address);

        if (lot.LotName.Equals(queryTrim, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrEmpty(queryCompact) && nameCompact == queryCompact))
            return 98;
        if (!string.IsNullOrEmpty(queryCompact) && queryCompact.Length >= 2)
        {
            if (nameCompact.StartsWith(queryCompact, StringComparison.OrdinalIgnoreCase)) return 90;
            if (nameCompact.Contains(queryCompact, StringComparison.OrdinalIgnoreCase)) return 85;
            if (stationCompact.Contains(queryCompact, StringComparison.OrdinalIgnoreCase)) return 82;
            if (addrCompact.Contains(queryCompact, StringComparison.OrdinalIgnoreCase)) return 75;
        }

        var tokens = ParkingFuzzySearchService.Tokenize(queryTrim)
            .Where(t => t.Length >= 2 && !StationNoiseWords.Contains(t))
            .ToArray();
        if (tokens.Length == 0) return 0;

        var combined = $"{lot.LotName} {lot.Address} {lot.NearestStationName} {lot.Description}";
        var matched = tokens.Count(t => combined.Contains(t, StringComparison.OrdinalIgnoreCase));
        if (matched == tokens.Length) return 72;
        return 0;
    }

    private static readonly HashSet<string> StationNoiseWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "lrt", "mrt", "monorail", "brt", "station", "stn", "near", "closest", "parking", "lot", "the", "for", "at", "in", "to"
    };

    private static string? GetPrimaryImageUrl(ParkingSpot spot) =>
        spot.ParkingSpotImages.FirstOrDefault(i => i.IsPrimary)?.MediaFile?.SecureUrl
        ?? spot.ParkingSpotImages.FirstOrDefault()?.MediaFile?.SecureUrl;

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

    private static bool OptionalBool(JsonObject args, string key, bool fallback)
    {
        if (args[key] is not JsonValue v) return fallback;
        if (v.TryGetValue<bool>(out var b)) return b;
        if (v.TryGetValue<string>(out var s) && bool.TryParse(s, out var parsed)) return parsed;
        return fallback;
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

    private static DateOnly RequireDate(JsonObject args, string key) =>
        OptionalDate(args, key) ?? throw new ToolArgumentException($"'{key}' is required.");

    private static DateOnly? OptionalDate(JsonObject args, string key)
    {
        var s = OptionalString(args, key, 10);
        if (s == null) return null;
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
    private static JsonObject Bool(string description) => new() { ["type"] = "boolean", ["description"] = description };

    private static TimeZoneInfo ResolveKlTimeZone()
    {
        foreach (var id in new[] { "Asia/Kuala_Lumpur", "Singapore Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("MYT", TimeSpan.FromHours(8), "MYT", "MYT");
    }

    private sealed record SpotBundle(
        ParkingSpot Spot,
        string? PrimaryImageUrl,
        List<Booking> BlockingBookings);

    private sealed record MatchedPrivateSpot(
        SpotBundle Bundle,
        double DistanceMeters,
        int TextScore,
        string StationName,
        string StopId,
        bool? AvailableOnRequestedDate,
        DateOnly? NextAvailableDate,
        string? StatusReason);

    private sealed record MatchedPublicLot(
        PublicParkingLotDto Lot,
        double DistanceMeters,
        int TextScore,
        string StationLabel);

    private sealed class ToolArgumentException(string message) : Exception(message);
}
