using System.Text.Json;
using ParkJomV2.Data;
using ParkJomV2.Models;

namespace ParkJomV2.Services;

/// <summary>Writes backend-side IoT actions into the existing IoTStatusLogs table.</summary>
internal static class IoTDeviceLogWriter
{
    public static void Add(
        ApplicationDbContext db,
        IoTDevice device,
        string action,
        string result,
        DateTime loggedAt,
        int? bookingId = null,
        string? details = null)
    {
        var payload = JsonSerializer.Serialize(new
        {
            source = "backend",
            action,
            result,
            bookingId,
            details
        });

        db.IoTStatusLogs.Add(new IoTStatusLog
        {
            IoTDeviceId = device.IoTDeviceId,
            DeviceStatus = device.DeviceStatus,
            Topic = $"backend/{action}",
            Message = payload,
            LoggedAt = loggedAt,
            CreatedAt = loggedAt,
            UpdatedAt = loggedAt
        });
    }
}
