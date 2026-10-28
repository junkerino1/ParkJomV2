using System.Text.Json.Serialization;

namespace ParkJomV2.DTOs;

public class GtfsRealtimeFeedResponseDto
{
    public string Agency { get; set; } = string.Empty;
    public string? Category { get; set; }
    public long FeedTimestamp { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int TotalVehicles { get; set; }
    public List<GtfsRealtimeVehicleDto> Vehicles { get; set; } = new();
}

public class GtfsRealtimeVehicleDto
{
    public string Id { get; set; } = string.Empty;
    public string VehicleId { get; set; } = string.Empty;
    public string? LicensePlate { get; set; }
    public string? Label { get; set; }
    public string? TripId { get; set; }
    public string? RouteId { get; set; }
    public string Agency { get; set; } = string.Empty;
    public string? Category { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public float? Bearing { get; set; }
    public double? SpeedKmh { get; set; }
    public double? Odometer { get; set; }
    public string? CurrentStatus { get; set; }
    public string? StopId { get; set; }
    public uint? CurrentStopSequence { get; set; }
    public string? CongestionLevel { get; set; }
    public DateTime Timestamp { get; set; }
    public double? DistanceMeters { get; set; }
    public int? EstimatedArrivalMinutes { get; set; }
}

public class GtfsRealtimeAgencyInfo
{
    public string AgencyKey { get; set; } = string.Empty;
    public string AgencyName { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty; // Train, Bus
    public List<string> Categories { get; set; } = new();
    public string Description { get; set; } = string.Empty;
}

public class GtfsRealtimeGeoJsonFeature
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "Feature";

    [JsonPropertyName("properties")]
    public GtfsRealtimeVehicleDto Properties { get; set; } = null!;

    [JsonPropertyName("geometry")]
    public GtfsRealtimeGeometry Geometry { get; set; } = null!;
}

public class GtfsRealtimeGeometry
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "Point";

    [JsonPropertyName("coordinates")]
    public List<double> Coordinates { get; set; } = new(); // [longitude, latitude]
}

public class GtfsRealtimeGeoJsonCollection
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "FeatureCollection";

    [JsonPropertyName("properties")]
    public Dictionary<string, object> Properties { get; set; } = new();

    [JsonPropertyName("features")]
    public List<GtfsRealtimeGeoJsonFeature> Features { get; set; } = new();
}
