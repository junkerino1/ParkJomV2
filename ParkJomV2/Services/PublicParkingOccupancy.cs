using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ParkJomV2.Models;

namespace ParkJomV2.Services;

public sealed record PublicParkingOccupancyReading(string IotDeviceUuid, int? OccupiedCount, DateTimeOffset ObservedAt)
{
    public static PublicParkingOccupancyReading Parse(string payload, DateTimeOffset now)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) {
            throw new ArgumentException("Occupancy payload is invalid.");
        }
        string? Text(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        if (!Guid.TryParseExact(Text("iotDeviceUuid"), "D", out var deviceUuid)) { 
            throw new ArgumentException("Device UUID is invalid.");
        }
        if (!root.TryGetProperty("occupiedCount", out var count) ||
            (count.ValueKind != JsonValueKind.Null && (count.ValueKind != JsonValueKind.Number || !count.TryGetInt32(out var parsed) || parsed < 0)))
            throw new ArgumentException("Unable to read occupiedCount. Please ensure it is a non-negative integer or null.");
        var observed = Text("observedAt");
        if (observed is null || !Regex.IsMatch(observed, @"(?:Z|[+-]\d{2}:\d{2})$", RegexOptions.IgnoreCase) ||
            !DateTimeOffset.TryParse(observed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp) ||
            timestamp > now.AddSeconds(10))
            throw new ArgumentException("Timestamp is invalid.");
        return new(deviceUuid.ToString("D"), count.ValueKind == JsonValueKind.Null ? null : count.GetInt32(), timestamp.ToUniversalTime());
    }
}

public sealed record PublicParkingOccupancySummary(int? OccupiedSpaces, int? AvailableSpaces, string Status, DateTimeOffset? UpdatedAt);

public static class PublicParkingOccupancy
{
    public static void BindDevice(PublicParkingLot lot, string deviceUuid)
    {
        deviceUuid = Guid.TryParseExact(deviceUuid.Trim(), "D", out var uuid)
            ? uuid.ToString("D") : deviceUuid.Trim();
        if (!string.Equals(lot.IotDeviceUuid, deviceUuid, StringComparison.OrdinalIgnoreCase))
        {
            lot.SensorOccupiedCount = null;
            lot.OccupancyObservedAt = null;
        }
        lot.IotDeviceUuid = deviceUuid;
    }

    public static bool Apply(PublicParkingLot lot, PublicParkingOccupancyReading reading)
    {
        if (!lot.IsActive || string.IsNullOrWhiteSpace(lot.IotDeviceUuid) ||
            !string.Equals(lot.IotDeviceUuid, reading.IotDeviceUuid, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Occupancy publisher is not bound to this active lot.");
        if (reading.OccupiedCount is < 0 || reading.OccupiedCount > lot.TotalCapacity)
            throw new ArgumentException("occupiedCount must be between zero and the lot's total capacity.");
        if (lot.OccupancyObservedAt.HasValue && reading.ObservedAt <= lot.OccupancyObservedAt.Value) return false;
        lot.SensorOccupiedCount = reading.OccupiedCount;
        lot.OccupancyObservedAt = reading.ObservedAt;
        return true;
    }

    public static PublicParkingOccupancySummary Summarize(PublicParkingLot lot, DateTimeOffset now)
    {
        if (!lot.SensorOccupiedCount.HasValue) return new(null, null, "unknown", lot.OccupancyObservedAt);
        var fresh = lot.OccupancyObservedAt >= now.AddSeconds(-90);
        return new(lot.SensorOccupiedCount, Math.Max(0, lot.TotalCapacity - lot.SensorOccupiedCount.Value),
            fresh ? "live" : "stale", lot.OccupancyObservedAt);
    }
}
