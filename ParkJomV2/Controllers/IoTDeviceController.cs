using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.DTOs;
using ParkJomV2.Services;

namespace ParkJomV2.Controllers;

/// <summary>HTTP boundary for prototype registration, OTP issuance, and owner device binding.</summary>
[ApiController]
[Route("api/iot")]
public sealed class IoTDeviceController : ControllerBase
{
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
    [Authorize]
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
    private IActionResult OperationError(IoTDeviceOperationException exception)
        => StatusCode(exception.StatusCode, new IoTApiResponse<object>
        {
            Code = exception.StatusCode,
            Success = false,
            Message = exception.Message
        });
}
