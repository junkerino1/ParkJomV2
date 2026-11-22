using System.Text.Json;

namespace ParkJomV2.Services;

/// <summary>A validated device reading shared by stored status, live scans, and MQTT ingestion.</summary>
internal sealed record IoTStatusReading(double? DistanceCm, string SensorStatus, string BollardPosition)
{
    public static bool TryCreate(double? distanceCm, string? position, string? result, out IoTStatusReading? reading)
    {
        reading = null;
        position = position?.ToUpperInvariant();
        if (position is not ("LOCKED" or "UNLOCKED")) return false;
        if (string.Equals(result, "SENSOR_READING_UNAVAILABLE", StringComparison.OrdinalIgnoreCase))
        {
            reading = new(null, "unavailable", position);
            return true;
        }
        // Telemetry has no result field; ACKs must explicitly report SUCCESS.
        if (result is not null && !string.Equals(result, "SUCCESS", StringComparison.OrdinalIgnoreCase)) return false;
        if (distanceCm is not double distance || !double.IsFinite(distance) || distance < 0) return false;
        reading = new(distance, "ok", position);
        return true;
    }

    public static bool TryParse(string? payload, out IoTStatusReading? reading)
    {
        reading = null;
        if (string.IsNullOrWhiteSpace(payload)) return false;
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            string? Text(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() : null;
            var result = Text("result") ?? Text("status");
            if (root.TryGetProperty("command", out _) && result is null) return false;
            var hasDistance = root.TryGetProperty("distanceCm", out var distanceElement);
            var position = Text("bollardPosition");
            // Movement ACKs report position but the current firmware omits their distance.
            if (!hasDistance && Text("command")?.ToUpperInvariant() is "LOCK" or "UNLOCK" or "AUTO_LOCK" &&
                string.Equals(result, "SUCCESS", StringComparison.OrdinalIgnoreCase) &&
                position?.ToUpperInvariant() is "LOCKED" or "UNLOCKED")
            {
                reading = new(null, "unknown", position.ToUpperInvariant());
                return true;
            }
            // Periodic telemetry reports a failed distance sensor as JSON null, without a result field.
            if (result is null && hasDistance && distanceElement.ValueKind == JsonValueKind.Null)
                result = "SENSOR_READING_UNAVAILABLE";
            double? distance = hasDistance &&
                distanceElement.ValueKind == JsonValueKind.Number && distanceElement.TryGetDouble(out var parsed)
                ? parsed : null;
            return TryCreate(distance, position, result, out reading);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
