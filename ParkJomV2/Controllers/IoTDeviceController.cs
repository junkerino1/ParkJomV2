using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using ParkJomV2.DTOs;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services;

namespace ParkJomV2.Controllers;

/// <summary>HTTP endpoints for device inventory, registration, and owner device binding.</summary>
[ApiController]
[Route("api/iot")]
public sealed class IoTDeviceController : ControllerBase
{
    public const string BindRateLimitPolicy = "iot-bind";
    private readonly IoTDeviceService _devices;
    private readonly CurrentUserService _currentUser;
    private readonly ILogger<IoTDeviceController> _logger;

    public IoTDeviceController(
        IoTDeviceService devices,
        CurrentUserService currentUser,
        ILogger<IoTDeviceController> logger)
    {
        _devices = devices;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>Lists bollards linked to parking bays owned by the signed-in property owner.</summary>
    [Authorize(Policy = "PropertyOwnerOnly")]
    [HttpGet("~/api/owner/iot/devices")]
    public async Task<ActionResult<IoTApiResponse<List<IoTDeviceListItem>>>> ListOwnerDevices(
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not int ownerId)
        {
            return Unauthorized();
        }

        var devices = await _devices.ListForOwnerAsync(ownerId, cancellationToken);
        return Ok(DeviceListResponse(devices));
    }

    /// <summary>Lists all registered bollards, including unassigned devices.</summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpGet("~/api/admin/iot/devices")]
    public async Task<ActionResult<IoTApiResponse<List<IoTDeviceListItem>>>> ListAdminDevices(
        CancellationToken cancellationToken)
    {
        var devices = await _devices.ListAllAsync(cancellationToken);
        return Ok(DeviceListResponse(devices));
    }

    /// <summary>Reads the latest logged distance, lock position, and heartbeat status for exactly one target.</summary>
    /// <remarks>bookingId requires the booking renter's token and a confirmed/active booking.
    /// deviceUuid or parkingSpotId requires an admin or the parking spot owner's token.
    /// Reads stored ACK/telemetry logs only; use POST status/scan for a fresh device reading.</remarks>
    [Authorize]
    [HttpGet("status")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public Task<ActionResult<IoTApiResponse<IoTStatusResponse>>> GetDeviceStatus(
        [FromQuery] IoTDeviceTarget target, CancellationToken cancellationToken
    ) => ReadStatusAsync(target, null, cancellationToken);

    /// <summary>Reads fresh distance and lock position together without moving the bollard.</summary>
    [Authorize]
    [HttpPost("{uuid}/status/scan")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public Task<ActionResult<IoTApiResponse<IoTStatusResponse>>> ScanDeviceStatus(
        string uuid, [FromServices] IMqttService mqtt, CancellationToken cancellationToken)
        => ReadStatusAsync(new IoTDeviceTarget { DeviceUuid = uuid }, mqtt, cancellationToken);

    private async Task<ActionResult<IoTApiResponse<IoTStatusResponse>>> ReadStatusAsync(
        IoTDeviceTarget target, IMqttService? mqtt, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not int userId)
            return Unauthorized();
        if (!Enum.TryParse<UserType>(User.FindFirst("UserType")?.Value, out var userType))
            return Forbid();
        try
        {
            var status = mqtt is null
                ? await _devices.GetStatusAsync(target, userId, userType, cancellationToken)
                : await _devices.ScanStatusAsync(target, userId, userType, mqtt, cancellationToken);
            return Ok(new IoTApiResponse<IoTStatusResponse>
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = status.ReceivedAt is null ? "No device reading has been recorded yet."
                    : mqtt is null ? "Latest logged bollard status retrieved."
                    : status.SensorStatus == "ok" ? "Live bollard status received."
                    : "Bollard position received; the distance sensor is unavailable.",
                Data = status
            });
        }
        catch (IoTDeviceOperationException ex)
        {
            return OperationError(ex);
        }
    }

    /// <summary>Searches the bollard fleet with booking, parking, property and owner context.</summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpGet("~/api/admin/iot/devices/search")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<IoTApiResponse<AdminIoTSupportPage>>> SearchSupportDevices(
        [FromQuery, Required, StringLength(30)] string by,
        [FromQuery, Required, StringLength(120)] string q,
        CancellationToken cancellationToken,
        [FromQuery, Range(1, 10_000)] int page = 1,
        [FromQuery, Range(1, 50)] int pageSize = 20)
    {
        try
        {
            var results = await _devices.SearchForSupportAsync(by, q, page, pageSize, cancellationToken);
            return Ok(new IoTApiResponse<AdminIoTSupportPage>
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Bollard search completed.",
                Data = results
            });
        }
        catch (IoTDeviceOperationException ex)
        {
            return OperationError(ex);
        }
    }

    private static IoTApiResponse<List<IoTDeviceListItem>> DeviceListResponse(List<IoTDeviceListItem> devices)
        => new()
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Bollards retrieved.",
            Data = devices
        };

    /// <summary>Registers a UUID and returns a generated device secret only when one is first created.</summary>
    /// <remarks>
    /// The ESP32 must save data.deviceSecret in NVS immediately. Later registration responses omit the secret
    /// and preserve the existing parking-spot assignment.
    /// </remarks>
    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> RegisterDevice(
        [FromBody] IoTDeviceUuidRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var (device, created, issuedSecret) = await _devices.RegisterAsync(request.DeviceUuid, cancellationToken);
            var statusCode = created ? StatusCodes.Status201Created : StatusCodes.Status200OK;
            return StatusCode(statusCode, new IoTApiResponse<IoTDeviceRegistrationResponse>
            {
                Code = statusCode,
                Success = true,
                Message = created
                    ? "Device registered and secret provisioned."
                    : issuedSecret is not null
                        ? "Existing device secret provisioned; assignment preserved."
                        : "Device already registered; existing secret and assignment preserved.",
                Data = new IoTDeviceRegistrationResponse
                {
                    DeviceUuid = device.DeviceUuid,
                    DeviceStatus = device.DeviceStatus,
                    IsAssigned = device.IsAssigned,
                    ParkingSpotId = device.ParkingSpotId,
                    DeviceSecret = issuedSecret
                }
            });
        }
        catch (IoTDeviceOperationException ex)
        {
            return OperationError(ex);
        }
        catch (DbUpdateException ex)
        {
            // Give a useful API error for schema mismatches while keeping SQL details in server logs.
            _logger.LogError(ex, "Device registration failed while saving UUID {DeviceUuid}", request.DeviceUuid);
            const int statusCode = StatusCodes.Status500InternalServerError;
            return StatusCode(statusCode, new IoTApiResponse<object>
            {
                Code = statusCode,
                Success = false,
                Message = "Database registration failed. Confirm the latest IoT migration is applied and check backend logs."
            });
        }
    }

    /// <summary>Issues a short-lived display code to a registered device, subject to a per-device cooldown.</summary>
    [AllowAnonymous]
    [HttpPost("gen-otp")]
    public async Task<IActionResult> GenerateOtp(
        [FromBody] IoTDeviceUuidRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var otp = await _devices.GenerateOtpAsync(request.DeviceUuid, cancellationToken);
            return Ok(new IoTApiResponse<IoTOtpResponse>
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Binding OTP generated. It is valid for five minutes.",
                Data = otp
            });
        }
        catch (IoTDeviceOperationException ex)
        {
            return OperationError(ex);
        }
    }

    /// <summary>Requires an authenticated property owner and consumes the OTP in the same transaction as binding.</summary>
    [Authorize(Policy = "PropertyOwnerOnly")]
    [EnableRateLimiting(BindRateLimitPolicy)]
    [HttpPost("bind")]
    public async Task<IActionResult> BindDevice(
        [FromBody] BindIoTDeviceRequest request,
        CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        if (!userId.HasValue)
        {
            return Unauthorized();
        }

        try
        {
            var device = await _devices.BindAsync(request, userId.Value, cancellationToken);
            return Ok(new IoTApiResponse<IoTDeviceResponse>
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Device bound to parking spot.",
                Data = device
            });
        }
        catch (IoTDeviceOperationException ex)
        {
            return OperationError(ex);
        }
    }

    // Centralizes the project's standard response envelope for expected validation failures.
    private ObjectResult OperationError(IoTDeviceOperationException exception)
        => StatusCode(exception.StatusCode, new IoTApiResponse<object>
        {
            Code = exception.StatusCode,
            Success = false,
            Message = exception.Message
        });
}
