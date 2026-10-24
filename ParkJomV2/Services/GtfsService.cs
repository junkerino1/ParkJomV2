using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.Services;

public class GtfsService
{
    private record RouteMeta(string Name, string ShortName, string Category, string Color, string First, string Terminal);

    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<GtfsService> _logger;

    private readonly List<GtfsRouteDto> _routes = new();
    private readonly List<GtfsStopDto> _stops = new();
    private readonly Dictionary<string, GtfsRouteShapeDto> _routeShapes = new(StringComparer.OrdinalIgnoreCase);
    private string _rawGeoJsonLines = string.Empty;
    private string _rawGeoJsonStops = string.Empty;
    private bool _isInitialized = false;
    private readonly object _lock = new();

    // Line definitions metadata
    private static readonly Dictionary<string, RouteMeta> LineMetadata =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["KJ"] = new("Kelana Jaya Line", "KJL", "LRT", "#D50032", "Gombak", "Putra Heights"),
            ["KJL"] = new("Kelana Jaya Line", "KJL", "LRT", "#D50032", "Gombak", "Putra Heights"),
            ["AG"] = new("Ampang Line", "AGL", "LRT", "#e57200", "Sentul Timur", "Ampang"),
            ["AGL"] = new("Ampang Line", "AGL", "LRT", "#e57200", "Sentul Timur", "Ampang"),
            ["SP"] = new("Sri Petaling Line", "SPL", "LRT", "#76232f", "Sentul Timur", "Putra Heights"),
            ["SPL"] = new("Sri Petaling Line", "SPL", "LRT", "#76232f", "Sentul Timur", "Putra Heights"),
            ["PH"] = new("Sri Petaling Line", "SPL", "LRT", "#76232f", "Sentul Timur", "Putra Heights"),
            ["KG"] = new("MRT Kajang Line", "KGL", "MRT", "#047940", "Kwasa Damansara", "Kajang"),
            ["KGL"] = new("MRT Kajang Line", "KGL", "MRT", "#047940", "Kwasa Damansara", "Kajang"),
            ["PY"] = new("MRT Putrajaya Line", "PYL", "MRT", "#FFCD00", "Kwasa Damansara", "Putrajaya Sentral"),
            ["PYL"] = new("MRT Putrajaya Line", "PYL", "MRT", "#FFCD00", "Kwasa Damansara", "Putrajaya Sentral"),
            ["MR"] = new("KL Monorail Line", "MRL", "Monorail", "#84bd00", "KL Sentral", "Titiwangsa"),
            ["MRL"] = new("KL Monorail Line", "MRL", "Monorail", "#84bd00", "KL Sentral", "Titiwangsa"),
            ["BRT"] = new("Sunway BRT Line", "BRT", "BRT", "#115740", "Sunway-Setia Jaya", "USJ 7"),
            ["SA"] = new("LRT Shah Alam Line (LRT3)", "SAL", "LRT", "#FF00FF", "Bandar Utama", "Johan Setia"),
            ["SAL"] = new("LRT Shah Alam Line (LRT3)", "SAL", "LRT", "#FF00FF", "Bandar Utama", "Johan Setia")
        };

    public GtfsService(IWebHostEnvironment environment, ILogger<GtfsService> logger)
    {
        _environment = environment;
        _logger = logger;
        Initialize();
    }

    private void Initialize()
    {
        if (_isInitialized) return;

        lock (_lock)
        {
            if (_isInitialized) return;

            try
            {
                var baseDir = AppContext.BaseDirectory;
                var contentDir = _environment.ContentRootPath;

                var gtfsFolder = FindGtfsFolder(baseDir, contentDir);
                if (string.IsNullOrEmpty(gtfsFolder) || !Directory.Exists(gtfsFolder))
                {
                    _logger.LogWarning("GTFS folder not found in probe paths. BaseDir: {BaseDir}", baseDir);
                    return;
                }

                _logger.LogInformation("Loading GTFS datasets from {Folder}", gtfsFolder);

                var linesPath = Path.Combine(gtfsFolder, "kl_rail_lines.json");
                var stopsPath = Path.Combine(gtfsFolder, "kl_rail_stops.json");
                var stationMetadataPath = Path.Combine(gtfsFolder, "mrt_lrt_stations.json");

                // 1. Load Raw GeoJSON lines
                if (File.Exists(linesPath))
                {
                    _rawGeoJsonLines = File.ReadAllText(linesPath);
                    ParseRailLines(_rawGeoJsonLines);
                }

                // 2. Load Raw GeoJSON stops
                if (File.Exists(stopsPath))
                {
                    _rawGeoJsonStops = File.ReadAllText(stopsPath);
                    ParseRailStops(_rawGeoJsonStops);
                }

                // 3. Enrich stops with multilingual names & wheelchair data from mrt_lrt_stations.json
                if (File.Exists(stationMetadataPath))
                {
                    EnrichStopsMetadata(File.ReadAllText(stationMetadataPath));
                }

                _isInitialized = true;
                _logger.LogInformation("GTFS Service initialized successfully. Loaded {RouteCount} lines, {StopCount} stops.",
                    _routes.Count, _stops.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize GTFS dataset.");
            }
        }
    }

    private static string? FindGtfsFolder(string baseDir, string contentDir)
    {
        var probePaths = new[]
        {
            Path.Combine(baseDir, "Data", "Gtfs"),
            Path.Combine(contentDir, "Data", "Gtfs"),
            Path.Combine(contentDir, "ParkJomV2", "Data", "Gtfs"),
            Path.Combine(contentDir, "data"),
            Path.Combine(baseDir, "..", "..", "..", "Data", "Gtfs"),
            Path.Combine(baseDir, "..", "..", "..", "data")
        };

        foreach (var path in probePaths)
        {
            var fullPath = Path.GetFullPath(path);
            if (Directory.Exists(fullPath) && File.Exists(Path.Combine(fullPath, "kl_rail_stops.json")))
            {
                return fullPath;
            }
        }

        return null;
    }

    private void ParseRailLines(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("features", out var features)) return;

            foreach (var feature in features.EnumerateArray())
            {
                if (!feature.TryGetProperty("properties", out var props)) continue;
                var routeId = props.TryGetProperty("route_id", out var rId) ? rId.GetString() ?? "" : "";
                var routeName = props.TryGetProperty("route_name", out var rName) ? rName.GetString() ?? "" : "";
                var routeColor = props.TryGetProperty("route_color", out var rCol) ? rCol.GetString() ?? "" : "";

                if (string.IsNullOrEmpty(routeId)) continue;

                // Extract LineString coordinates
                var coordList = new List<List<double>>();
                if (feature.TryGetProperty("geometry", out var geom) &&
                    geom.TryGetProperty("coordinates", out var coords) &&
                    coords.ValueKind == JsonValueKind.Array)
                {
                    foreach (var point in coords.EnumerateArray())
                    {
                        if (point.ValueKind == JsonValueKind.Array)
                        {
                            var pointCoords = new List<double>();
                            foreach (var c in point.EnumerateArray())
                            {
                                if (c.TryGetDouble(out var val)) pointCoords.Add(val);
                            }
                            if (pointCoords.Count >= 2) coordList.Add(pointCoords);
                        }
                    }
                }

                if (!_routeShapes.ContainsKey(routeId))
                {
                    _routeShapes[routeId] = new GtfsRouteShapeDto
                    {
                        RouteId = routeId,
                        RouteName = routeName,
                        RouteColor = routeColor,
                        Coordinates = coordList
                    };
                }
                else
                {
                    // If multi-part LineString, append
                    _routeShapes[routeId].Coordinates.AddRange(coordList);
                }
            }

            // Build canonical routes summary list
            var distinctRouteIds = _routeShapes.Keys.ToList();
            foreach (var rId in distinctRouteIds)
            {
                var canonicalId = NormalizeRouteId(rId);
                var meta = LineMetadata.TryGetValue(canonicalId, out var m)
                    ? m
                    : new RouteMeta(rId, rId, "Rail Transit", _routeShapes[rId].RouteColor, "", "");

                if (!_routes.Any(r => r.RouteId.Equals(canonicalId, StringComparison.OrdinalIgnoreCase)))
                {
                    _routes.Add(new GtfsRouteDto
                    {
                        RouteId = canonicalId,
                        RouteName = meta.Name,
                        ShortName = meta.ShortName,
                        Category = meta.Category,
                        Color = !string.IsNullOrEmpty(meta.Color) ? meta.Color : _routeShapes[rId].RouteColor,
                        FirstStation = meta.First,
                        TerminalStation = meta.Terminal,
                        OperatingHours = "06:00 - 23:50",
                        Operator = "Rapid KL"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing kl_rail_lines.json");
        }
    }

    private void ParseRailStops(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("features", out var features)) return;

            foreach (var feature in features.EnumerateArray())
            {
                if (!feature.TryGetProperty("properties", out var props)) continue;
                var stopId = props.TryGetProperty("stop_id", out var sId) ? sId.GetString()?.Trim() ?? "" : "";
                var stopName = props.TryGetProperty("stop_name", out var sName) ? sName.GetString()?.Trim() ?? "" : "";

                double lng = 0, lat = 0;
                if (feature.TryGetProperty("geometry", out var geom) &&
                    geom.TryGetProperty("coordinates", out var coords) &&
                    coords.ValueKind == JsonValueKind.Array)
                {
                    var arr = coords.EnumerateArray().ToList();
                    if (arr.Count >= 2)
                    {
                        arr[0].TryGetDouble(out lng);
                        arr[1].TryGetDouble(out lat);
                    }
                }

                if (string.IsNullOrEmpty(stopId) || lat == 0 || lng == 0) continue;

                var routePrefix = ExtractRoutePrefix(stopId);
                var meta = LineMetadata.TryGetValue(routePrefix, out var m)
                    ? m
                    : new RouteMeta(routePrefix, routePrefix, "Transit", "#e57200", "", "");

                var stop = new GtfsStopDto
                {
                    StopId = stopId,
                    StopName = CapitalizeStopName(stopName),
                    RouteId = routePrefix,
                    RouteName = meta.Name,
                    RouteColor = meta.Color,
                    Category = meta.Category,
                    Latitude = lat,
                    Longitude = lng,
                    WheelchairAccessible = true,
                    OperatingHours = "06:00 - 23:50"
                };

                stop.MultilingualNames["en"] = stop.StopName;
                _stops.Add(stop);
            }

            // Sync total stops to routes
            foreach (var route in _routes)
            {
                var routeStops = _stops.Where(s => s.RouteId.Equals(route.RouteId, StringComparison.OrdinalIgnoreCase)).ToList();
                route.TotalStops = routeStops.Count;
                route.Stops = routeStops.Select((s, idx) => new GtfsStopSummaryDto
                {
                    StopId = s.StopId,
                    StopName = s.StopName,
                    Sequence = idx + 1,
                    Latitude = s.Latitude,
                    Longitude = s.Longitude
                }).ToList();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing kl_rail_stops.json");
        }
    }

    private void EnrichStopsMetadata(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("features", out var features)) return;

            foreach (var feature in features.EnumerateArray())
            {
                if (!feature.TryGetProperty("properties", out var props)) continue;
                var refCode = props.TryGetProperty("ref", out var rVal) ? rVal.GetString()?.Trim() ?? "" : "";
                var nameZh = props.TryGetProperty("name:zh", out var nZh) ? nZh.GetString() : null;
                var nameMs = props.TryGetProperty("name:ms", out var nMs) ? nMs.GetString() : null;
                var nameTa = props.TryGetProperty("name:ta", out var nTa) ? nTa.GetString() : null;
                var wheelchair = props.TryGetProperty("wheelchair", out var wc) ? wc.GetString() : null;

                if (string.IsNullOrEmpty(refCode)) continue;

                var matchingStops = _stops.Where(s =>
                    s.StopId.Equals(refCode, StringComparison.OrdinalIgnoreCase) ||
                    refCode.Contains(s.StopId, StringComparison.OrdinalIgnoreCase)).ToList();

                foreach (var stop in matchingStops)
                {
                    if (!string.IsNullOrEmpty(nameZh)) stop.MultilingualNames["zh"] = nameZh;
                    if (!string.IsNullOrEmpty(nameMs)) stop.MultilingualNames["ms"] = nameMs;
                    if (!string.IsNullOrEmpty(nameTa)) stop.MultilingualNames["ta"] = nameTa;
                    if (!string.IsNullOrEmpty(wheelchair))
                    {
                        stop.WheelchairAccessible = wheelchair.Equals("yes", StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not enrich stops with OpenStreetMap multilingual metadata.");
        }
    }

    public List<GtfsRouteDto> GetAllRoutes()
    {
        return _routes.OrderBy(r => r.RouteName).ToList();
    }

    public GtfsRouteDto? GetRouteById(string routeId)
    {
        var normalized = NormalizeRouteId(routeId);
        return _routes.FirstOrDefault(r => r.RouteId.Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    public GtfsRouteShapeDto? GetRouteShape(string routeId)
    {
        if (_routeShapes.TryGetValue(routeId, out var shape)) return shape;
        var normalized = NormalizeRouteId(routeId);
        return _routeShapes.TryGetValue(normalized, out var normShape) ? normShape : null;
    }

    public List<GtfsStopDto> GetAllStops(string? line = null, string? search = null, double? lat = null, double? lng = null, double? radiusMeters = null)
    {
        var query = _stops.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(line))
        {
            var normalizedLine = NormalizeRouteId(line);
            query = query.Where(s => s.RouteId.Equals(normalizedLine, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(s =>
                s.StopName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                s.StopId.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                s.MultilingualNames.Values.Any(v => v.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        if (lat.HasValue && lng.HasValue)
        {
            var radius = radiusMeters ?? 50000; // default 50km
            return query
                .Select(s =>
                {
                    var dist = CalculateDistanceMeters(lat.Value, lng.Value, s.Latitude, s.Longitude);
                    return new GtfsStopDto
                    {
                        StopId = s.StopId,
                        StopName = s.StopName,
                        RouteId = s.RouteId,
                        RouteName = s.RouteName,
                        RouteColor = s.RouteColor,
                        Category = s.Category,
                        Latitude = s.Latitude,
                        Longitude = s.Longitude,
                        WheelchairAccessible = s.WheelchairAccessible,
                        MultilingualNames = s.MultilingualNames,
                        OperatingHours = s.OperatingHours,
                        DistanceMeters = Math.Round(dist, 1),
                        WalkingTimeMinutes = (int)Math.Ceiling(dist / 80.0)
                    };
                })
                .Where(s => s.DistanceMeters <= radius)
                .OrderBy(s => s.DistanceMeters)
                .ToList();
        }

        return query.OrderBy(s => s.StopName).ToList();
    }

    public GtfsStopDto? GetStopById(string stopId)
    {
        return _stops.FirstOrDefault(s => s.StopId.Equals(stopId.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public List<GtfsStopDto> GetNearbyStations(double lat, double lng, double radiusMeters = 3000, int limit = 10)
    {
        return _stops
            .Select(s =>
            {
                var dist = CalculateDistanceMeters(lat, lng, s.Latitude, s.Longitude);
                return new GtfsStopDto
                {
                    StopId = s.StopId,
                    StopName = s.StopName,
                    RouteId = s.RouteId,
                    RouteName = s.RouteName,
                    RouteColor = s.RouteColor,
                    Category = s.Category,
                    Latitude = s.Latitude,
                    Longitude = s.Longitude,
                    WheelchairAccessible = s.WheelchairAccessible,
                    MultilingualNames = s.MultilingualNames,
                    OperatingHours = s.OperatingHours,
                    DistanceMeters = Math.Round(dist, 1),
                    WalkingTimeMinutes = (int)Math.Ceiling(dist / 80.0)
                };
            })
            .Where(s => s.DistanceMeters <= radiusMeters)
            .OrderBy(s => s.DistanceMeters)
            .Take(limit)
            .ToList();
    }

    public GtfsScheduleDto? GetStopSchedule(string stopId)
    {
        var stop = GetStopById(stopId);
        if (stop == null) return null;

        var klTime = DateTime.UtcNow.AddHours(8); // Malaysia Standard Time (UTC+8)

        var hour = klTime.Hour;
        var minute = klTime.Minute;
        var isOperating = hour >= 6 && (hour < 23 || (hour == 23 && minute <= 50));

        // Peak hours in Malaysia: 07:00-09:30 and 17:00-19:30 on weekdays
        var isWeekday = klTime.DayOfWeek != DayOfWeek.Saturday && klTime.DayOfWeek != DayOfWeek.Sunday;
        var isMorningPeak = isWeekday && (hour == 7 || hour == 8 || (hour == 9 && minute <= 30));
        var isEveningPeak = isWeekday && (hour == 17 || hour == 18 || (hour == 19 && minute <= 30));
        var isPeak = isMorningPeak || isEveningPeak;

        int freqMinutes = isPeak ? 3 : (hour >= 22 ? 10 : 6);

        var schedule = new GtfsScheduleDto
        {
            StopId = stop.StopId,
            StopName = stop.StopName,
            RouteId = stop.RouteId,
            RouteName = stop.RouteName,
            CurrentTime = klTime.ToString("HH:mm:ss"),
            IsOperating = isOperating,
            ServiceStatus = isOperating ? "Normal Service" : "Service Ended (Resumes at 06:00 AM)",
            IsPeakHour = isPeak,
            FrequencyMinutes = freqMinutes,
            OperatingHours = "06:00 - 23:50",
            FirstTrain = "06:00 AM",
            LastTrain = "11:50 PM"
        };

        if (isOperating)
        {
            var meta = LineMetadata.TryGetValue(stop.RouteId, out var m) ? m : new RouteMeta("", "", "", "", "Northbound", "Southbound");
            var destA = !string.IsNullOrEmpty(meta.First) ? meta.First : "Northbound Terminal";
            var destB = !string.IsNullOrEmpty(meta.Terminal) ? meta.Terminal : "Southbound Terminal";

            // Direction 1
            var wait1 = (minute % freqMinutes == 0) ? freqMinutes : (freqMinutes - (minute % freqMinutes));
            var depTime1 = klTime.AddMinutes(wait1);
            schedule.Departures.Add(new GtfsDepartureDto
            {
                Direction = $"To {destA}",
                Destination = destA,
                EstimatedDeparture = depTime1.ToString("HH:mm"),
                MinutesUntilArrival = wait1,
                TrainType = $"{stop.Category} 4-Car Standard",
                SubsequentDepartures = new List<string>
                {
                    depTime1.AddMinutes(freqMinutes).ToString("HH:mm"),
                    depTime1.AddMinutes(freqMinutes * 2).ToString("HH:mm"),
                    depTime1.AddMinutes(freqMinutes * 3).ToString("HH:mm")
                }
            });

            // Direction 2
            var wait2 = ((minute + 2) % freqMinutes == 0) ? freqMinutes : (freqMinutes - ((minute + 2) % freqMinutes));
            var depTime2 = klTime.AddMinutes(wait2);
            schedule.Departures.Add(new GtfsDepartureDto
            {
                Direction = $"To {destB}",
                Destination = destB,
                EstimatedDeparture = depTime2.ToString("HH:mm"),
                MinutesUntilArrival = wait2,
                TrainType = $"{stop.Category} 4-Car Standard",
                SubsequentDepartures = new List<string>
                {
                    depTime2.AddMinutes(freqMinutes).ToString("HH:mm"),
                    depTime2.AddMinutes(freqMinutes * 2).ToString("HH:mm"),
                    depTime2.AddMinutes(freqMinutes * 3).ToString("HH:mm")
                }
            });
        }

        return schedule;
    }

    public async Task<GtfsNearbyParkingDto?> GetNearbyParkingAsync(string stopId, double radiusMeters, ApplicationDbContext dbContext)
    {
        var stop = GetStopById(stopId);
        if (stop == null) return null;

        var radius = radiusMeters > 0 ? radiusMeters : 1500; // default 1.5km

        // Fetch published and approved parking spots with properties
        var properties = await dbContext.Properties
            .AsNoTracking()
            .Include(p => p.ParkingSpots)
                .ThenInclude(ps => ps.ParkingSpotImages)
                    .ThenInclude(psi => psi.MediaFile)
            .Include(p => p.ParkingSpots)
                .ThenInclude(ps => ps.IoTDevice)
            .Include(p => p.ParkingSpots)
                .ThenInclude(ps => ps.VerificationRequests.Where(vr => vr.IsCurrent))
            .ToListAsync();

        var result = new GtfsNearbyParkingDto
        {
            StopId = stop.StopId,
            StopName = stop.StopName,
            Latitude = stop.Latitude,
            Longitude = stop.Longitude,
            SearchRadiusMeters = radius
        };

        foreach (var prop in properties)
        {
            var dist = CalculateDistanceMeters(stop.Latitude, stop.Longitude, (double)prop.Latitude, (double)prop.Longitude);
            if (dist > radius) continue;

            var availableSpots = prop.ParkingSpots
                .Where(ps => ps.IsPublished
                          && ps.AvailabilityStatus == AvailabilityStatus.Available
                          && ps.VerificationRequests.Any(vr => vr.IsCurrent && vr.VerificationStatus == VerificationStatus.Approved))
                .ToList();

            foreach (var spot in availableSpots)
            {
                var primaryImage = spot.ParkingSpotImages.FirstOrDefault(i => i.IsPrimary)?.MediaFile?.SecureUrl
                                   ?? spot.ParkingSpotImages.FirstOrDefault()?.MediaFile?.SecureUrl;

                result.ParkingSpots.Add(new GtfsNearbySpotItemDto
                {
                    ParkingSpotId = spot.ParkingSpotId,
                    ParkingLabel = spot.ParkingLabel ?? $"Bay {spot.ParkingSpotId}",
                    PropertyId = prop.PropertyId,
                    PropertyName = prop.PropertyName,
                    Address = prop.Address,
                    DistanceMeters = Math.Round(dist, 1),
                    WalkingTimeMinutes = (int)Math.Ceiling(dist / 80.0),
                    DailyRate = spot.DailyRate,
                    MonthlyRate = spot.MonthlyRate,
                    AvailabilityStatus = spot.AvailabilityStatus.ToString(),
                    PrimaryImageUrl = primaryImage,
                    HasIoTDevice = spot.IoTDevice != null
                });
            }
        }

        result.ParkingSpots = result.ParkingSpots.OrderBy(p => p.DistanceMeters).ToList();
        result.TotalAvailableSpots = result.ParkingSpots.Count;

        return result;
    }

    public string GetRawGeoJsonLines() => _rawGeoJsonLines;
    public string GetRawGeoJsonStops() => _rawGeoJsonStops;

    public static double CalculateDistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000; // Earth radius in meters
        var dLat = (lat2 - lat1) * Math.PI / 180.0;
        var dLon = (lon2 - lon1) * Math.PI / 180.0;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private static string NormalizeRouteId(string routeId)
    {
        var upper = routeId.Trim().ToUpperInvariant();
        return upper switch
        {
            "KJL" => "KJ",
            "AGL" => "AG",
            "SPL" => "SP",
            "PH" => "SP",
            "KGL" => "KG",
            "PYL" => "PY",
            "MRL" => "MR",
            "SAL" => "SA",
            _ => upper
        };
    }

    private static string ExtractRoutePrefix(string stopId)
    {
        var match = System.Text.RegularExpressions.Regex.Match(stopId, @"^[A-Za-z]+");
        var prefix = match.Success ? match.Value.ToUpperInvariant() : "KJ";
        return NormalizeRouteId(prefix);
    }

    private static string CapitalizeStopName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return name;
        var words = name.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", words.Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1)));
    }
}
