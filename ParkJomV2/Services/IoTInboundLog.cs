using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;

namespace ParkJomV2.Services;

/// <summary>Recognizes repeated deliveries, without dropping scheduled readings with unchanged values.</summary>
internal static class IoTInboundLog
{
    public static TimeSpan DuplicateWindow(string payload, bool isAcknowledgement)
    {
        if (isAcknowledgement)
        {
            try
            {
                using var json = JsonDocument.Parse(payload);
                if (json.RootElement.ValueKind == JsonValueKind.Object &&
                    json.RootElement.TryGetProperty("commandId", out var id) &&
                    id.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(id.GetString()))
                    return TimeSpan.FromMinutes(10);
            }
            catch (JsonException) { }
        }
        return TimeSpan.FromSeconds(2);
    }

    // Caller holds the device row lock until the log and its side effects are committed.
    public static Task<DateTime?> FindDuplicateAsync(
        ApplicationDbContext db, int deviceId, string topic, string payload, bool isAcknowledgement, DateTime receivedAt)
    {
        var cutoff = receivedAt - DuplicateWindow(payload, isAcknowledgement);
        return db.IoTStatusLogs.AsNoTracking()
            .Where(log => log.IoTDeviceId == deviceId && log.Topic == topic &&
                log.Message == payload && log.LoggedAt >= cutoff && log.LoggedAt <= receivedAt)
            .OrderByDescending(log => log.LoggedAt)
            .Select(log => (DateTime?)log.LoggedAt)
            .FirstOrDefaultAsync();
    }
}
