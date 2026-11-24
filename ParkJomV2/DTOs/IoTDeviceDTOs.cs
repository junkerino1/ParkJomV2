using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ParkJomV2.DTOs;

// Device-facing requests use a persistent UUID in canonical form.
public sealed class IoTDeviceUuidRequest
{
    [Required]
    [StringLength(36, MinimumLength = 36)]
    public string DeviceUuid { get; set; } = string.Empty;
}

// The six-digit code identifies the device to pair with the owner's parking spot.
public sealed class BindIoTDeviceRequest
{
    [Range(1, int.MaxValue)]
    public int ParkingSpotId { get; set; }

    [Required]
    [RegularExpression("^[0-9]{6}$")]
    public string Otp { get; set; } = string.Empty;
}

// These response DTOs follow the existing API's Code/Success/Message/Data envelope.
public sealed class IoTDeviceResponse
{
    public string DeviceUuid { get; set; } = string.Empty;
    public string DeviceStatus { get; set; } = string.Empty;
    public bool IsAssigned { get; set; }
    public int? ParkingSpotId { get; set; }
}

public sealed class IoTDeviceRegistrationResponse
{
    public string DeviceUuid { get; set; } = string.Empty;
    public string DeviceStatus { get; set; } = string.Empty;
    public bool IsAssigned { get; set; }
    public int? ParkingSpotId { get; set; }

    // Returned only when registration creates a secret for this device.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DeviceSecret { get; set; }
}

public sealed class IoTOtpResponse
{
    public string Otp { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
}

/// <summary>Exactly one device selector must be supplied.</summary>
public sealed class IoTDeviceTarget : IValidatableObject
{
    public string? DeviceUuid { get; set; }

    [Range(1, int.MaxValue)]
    public int? BookingId { get; set; }

    [Range(1, int.MaxValue)]
    public int? ParkingSpotId { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var count = (DeviceUuid is not null ? 1 : 0) + (BookingId.HasValue ? 1 : 0) + (ParkingSpotId.HasValue ? 1 : 0);
        if (count != 1)
            yield return new ValidationResult("Supply exactly one of deviceUuid, bookingId, or parkingSpotId.");
        if (DeviceUuid is not null && !Guid.TryParseExact(DeviceUuid, "D", out _))
            yield return new ValidationResult("deviceUuid must be a valid UUID.", new[] { nameof(DeviceUuid) });
    }
}

public sealed record IoTStatusResponse(
    string DeviceUuid,
    int? BookingId,
    string HeartbeatStatus,
    DateTimeOffset? LastHeartbeatAt,
    DateTimeOffset CheckedAt,
    int OfflineAfterSeconds,
    double? DistanceCm,
    string SensorStatus,
    string? BollardPosition,
    DateTimeOffset? ReceivedAt,
    IoTStatusParkingSpotResponse? ParkingSpot);

public sealed record IoTStatusParkingSpotResponse(
    int ParkingSpotId,
    string? ParkingLabel,
    int PropertyId,
    string PropertyName);

public sealed class IoTDeviceListItem
{
    public int IoTDeviceId { get; init; }
    public string DeviceUuid { get; init; } = string.Empty;
    public string DeviceStatus { get; init; } = string.Empty;
    public string FirmwareVersion { get; init; } = string.Empty;
    public bool IsAssigned { get; init; }
    public int? ParkingSpotId { get; init; }
    public string? ParkingLabel { get; init; }
    public int? PropertyId { get; init; }
    public string? PropertyName { get; init; }
    public int? OwnerId { get; init; }
    public DateTime? LastHeartbeatAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public sealed class IoTApiResponse<T>
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }
}
