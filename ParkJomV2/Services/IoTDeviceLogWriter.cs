using System.Text.Json;
using System.Text.Json.Serialization;
using ParkJomV2.Data;
using ParkJomV2.Models;

namespace ParkJomV2.Services;

/// <summary>Writes backend-side IoT actions into the existing IoTStatusLogs table.</summary>
internal static class IoTDeviceLogWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // Persist outcomes and failures. Routine progress belongs in application diagnostics.
    public static bool ShouldPersist(string action, string result)
    {
        if (result is "received" or "passed" or "verified" or "consumed" or "published") return false;
        if (action is "device_ack_received" or "unassociated_device_command_ack" or "unlock_ack_booking_checkin") return false;
        if (action == "device_registration" && result == "already_registered") return false;
        if (action is "binding_otp_generated" or "unlock_challenge_created" && result == "success") return false;
        return true;
    }

    public static bool Add(
        ApplicationDbContext db,
        IoTDevice device,
        string action,
        string result,
        DateTime loggedAt,
        int? bookingId = null,
        string? details = null,
        object? data = null)
    {
        if (!ShouldPersist(action, result)) return false;
        var payload = JsonSerializer.Serialize(new
        {
            source = "backend",
            action,
            result,
            bookingId,
            details,
            data
        }, JsonOptions);

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
        return true;
    }
}
