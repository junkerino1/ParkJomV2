using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
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
    [HttpGet("owner/devices")]
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
    [HttpGet("admin/devices")]
    public async Task<ActionResult<IoTApiResponse<List<IoTDeviceListItem>>>> ListAdminDevices(
        CancellationToken cancellationToken)
    {
        var devices = await _devices.ListAllAsync(cancellationToken);
        return Ok(DeviceListResponse(devices));
    }

    /// <summary>Returns heartbeat freshness, the last valid distance scan, and the assigned parking spot.</summary>
    /// <remarks>Status is unknown until the first heartbeat, online for less than 90 seconds after a heartbeat, and offline after that.</remarks>
    [Authorize]
    [HttpGet("{uuid}/status")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IoTApiResponse<IoTHeartbeatStatusResponse>>> GetDeviceStatus(
        string uuid, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not int userId)
        {
            return Unauthorized();
        }

        var isAdmin = User.HasClaim("UserType", UserType.Admin.ToString());
        if (!isAdmin && !User.HasClaim("UserType", UserType.PropertyOwner.ToString()))
        {
            return Forbid();
        }

        try
        {
            var status = await _devices.GetHeartbeatStatusAsync(uuid, isAdmin ? null : userId, cancellationToken);
            return Ok(new IoTApiResponse<IoTHeartbeatStatusResponse>
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Device heartbeat status retrieved.",
                Data = status
            });
        }
        catch (IoTDeviceOperationException ex)
        {
            return OperationError(ex);
        }
    }

    /// <summary>Requests a fresh HC-SR04 distance reading without moving the bollard.</summary>
    [Authorize]
    [HttpPost("{uuid}/status/scan")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(StatusCodes.Status504GatewayTimeout)]
    public async Task<ActionResult<IoTApiResponse<IoTLiveStatusScanResponse>>> ScanDeviceStatus(
        string uuid, [FromServices] IMqttService mqtt, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not int userId)
            return Unauthorized();

        var isAdmin = User.HasClaim("UserType", UserType.Admin.ToString());
        if (!isAdmin && !User.HasClaim("UserType", UserType.PropertyOwner.ToString()))
            return Forbid();

        try
        {
            var scan = await _devices.RequestLiveStatusScanAsync(uuid, isAdmin ? null : userId, mqtt, cancellationToken);
            return Ok(new IoTApiResponse<IoTLiveStatusScanResponse>
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Fresh bollard distance received.",
                Data = scan
            });
        }
        catch (IoTDeviceOperationException ex)
        {
            return OperationError(ex);
        }
    }

    /// <summary>Reads the reported lock position from an owner's bollard without moving it.</summary>
    [Authorize(Policy = "PropertyOwnerOnly")]
    [HttpPost("{uuid}/position/scan")]
    public async Task<ActionResult<IoTApiResponse<IoTLivePositionResponse>>> ScanDevicePosition(
        string uuid, [FromServices] IMqttService mqtt, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not int ownerId)
            return Unauthorized();

        try
        {
            var position = await _devices.RequestLivePositionAsync(uuid, ownerId, mqtt, cancellationToken);
            return Ok(new IoTApiResponse<IoTLivePositionResponse>
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Bollard position received.",
                Data = position
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

    /// <summary>Registers a UUID once; repeat requests return the existing device without changing its assignment.</summary>
    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> RegisterDevice(
        [FromBody] IoTDeviceUuidRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var (device, created) = await _devices.RegisterAsync(request.DeviceUuid, cancellationToken);
            var statusCode = created ? StatusCodes.Status201Created : StatusCodes.Status200OK;
            return StatusCode(statusCode, new IoTApiResponse<IoTDeviceResponse>
            {
                Code = statusCode,
                Success = true,
                Message = created ? "Device registered." : "Device already registered; existing assignment preserved.",
                Data = device
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
