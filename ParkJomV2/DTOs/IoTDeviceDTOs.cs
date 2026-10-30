using System.ComponentModel.DataAnnotations;

namespace ParkJomV2.DTOs;

// Device-facing requests use a persistent UUID in canonical form.
public sealed class IoTDeviceUuidRequest
{
    [Required]
    [StringLength(36, MinimumLength = 36)]
    public string DeviceUuid { get; set; } = string.Empty;
}

// Owner binding pairs a device UUID and six-digit one-time code with one parking spot.
public sealed class BindIoTDeviceRequest
{
    [Required]
    [StringLength(36, MinimumLength = 36)]
    public string DeviceUuid { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int ParkingSpotId { get; set; }

    [Required]
    [RegularExpression("^\\d{6}$")]
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

public sealed class IoTOtpResponse
{
    public string Otp { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
}

public sealed class IoTApiResponse<T>
{
    public int Code { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public T? Data { get; set; }
}
