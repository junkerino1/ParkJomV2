using System.Text.Json.Serialization;

namespace ParkJomV2.DTOs;

public class GtfsRouteDto
{
    public string RouteId { get; set; } = string.Empty;
    public string RouteName { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty; // LRT, MRT, Monorail, BRT
    public string Color { get; set; } = string.Empty;
    public string Operator { get; set; } = "Rapid KL";
    public int TotalStops { get; set; }
    public string FirstStation { get; set; } = string.Empty;
    public string TerminalStation { get; set; } = string.Empty;
    public string OperatingHours { get; set; } = "06:00 - 23:50";
    public List<GtfsStopSummaryDto> Stops { get; set; } = new();
}

public class GtfsStopSummaryDto
{
    public string StopId { get; set; } = string.Empty;
    public string StopName { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

public class GtfsStopDto
{
    public string StopId { get; set; } = string.Empty;
    public string StopName { get; set; } = string.Empty;
    public string RouteId { get; set; } = string.Empty;
    public string RouteName { get; set; } = string.Empty;
    public string RouteColor { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public bool WheelchairAccessible { get; set; } = true;
    public Dictionary<string, string> MultilingualNames { get; set; } = new();
    public double? DistanceMeters { get; set; }
    public int? WalkingTimeMinutes { get; set; }
    public string OperatingHours { get; set; } = "06:00 - 23:50";
}

public class GtfsRouteShapeDto
{
    public string RouteId { get; set; } = string.Empty;
    public string RouteName { get; set; } = string.Empty;
    public string RouteColor { get; set; } = string.Empty;
    public List<List<double>> Coordinates { get; set; } = new();
}

public class GtfsScheduleDto
{
    public string StopId { get; set; } = string.Empty;
    public string StopName { get; set; } = string.Empty;
    public string RouteId { get; set; } = string.Empty;
    public string RouteName { get; set; } = string.Empty;
    public string CurrentTime { get; set; } = string.Empty;
    public bool IsOperating { get; set; }
    public string ServiceStatus { get; set; } = "Normal Service";
    public bool IsPeakHour { get; set; }
    public int FrequencyMinutes { get; set; }
    public string OperatingHours { get; set; } = "06:00 - 23:50";
    public string FirstTrain { get; set; } = "06:00 AM";
    public string LastTrain { get; set; } = "11:50 PM";
    public List<GtfsDepartureDto> Departures { get; set; } = new();
}

public class GtfsDepartureDto
{
    public string Direction { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public string EstimatedDeparture { get; set; } = string.Empty;
    public int MinutesUntilArrival { get; set; }
    public string TrainType { get; set; } = "Standard Transit Train";
    public List<string> SubsequentDepartures { get; set; } = new();
}

public class GtfsNearbyParkingDto
{
    public string StopId { get; set; } = string.Empty;
    public string StopName { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double SearchRadiusMeters { get; set; }
    public int TotalAvailableSpots { get; set; }
    public List<GtfsNearbySpotItemDto> ParkingSpots { get; set; } = new();
}

public class GtfsNearbySpotItemDto
{
    public int ParkingSpotId { get; set; }
    public string ParkingLabel { get; set; } = string.Empty;
    public int PropertyId { get; set; }
    public string PropertyName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public double DistanceMeters { get; set; }
    public int WalkingTimeMinutes { get; set; }
    public decimal? DailyRate { get; set; }
    public decimal? MonthlyRate { get; set; }
    public string AvailabilityStatus { get; set; } = "Available";
    public string? PrimaryImageUrl { get; set; }
    public bool HasIoTDevice { get; set; }
}
