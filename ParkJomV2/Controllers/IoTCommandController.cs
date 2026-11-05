using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services;

namespace ParkJomV2.Controllers;

[ApiController]
[Authorize]
[Route("api/iot/bookings")]
public sealed class IoTCommandController : ControllerBase
{
    private const string DefaultBleServiceUuid = "6e400001-b5a3-f393-e0a9-e50e24dcca9e";
    private readonly ApplicationDbContext _db;
    private readonly CurrentUserService _currentUser;
    private readonly IMqttService _mqtt;
    private readonly AccessLogService _accessLogs;
    private readonly UnlockChallengeService _challenges;
    private readonly IConfiguration _configuration;
    private readonly ILogger<IoTCommandController> _logger;

    public IoTCommandController(
        ApplicationDbContext db,
        CurrentUserService currentUser,
        IMqttService mqtt,
        AccessLogService accessLogs,
        UnlockChallengeService challenges,
        IConfiguration configuration,
        ILogger<IoTCommandController> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _mqtt = mqtt;
        _accessLogs = accessLogs;
        _challenges = challenges;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>Generates a short-lived BLE challenge for a renter's booking.</summary>
    /// <remarks>
    /// The renter must own a confirmed or active booking, have an assigned device, and request access during the booking's Malaysia-time availability window. GPS is checked here and again when unlocking. Any pending overstay debt is automatically settled from the wallet if funds are sufficient; otherwise this endpoint returns PENALTY_DUE. The backend sends the nonce to the assigned ESP32 over MQTT; it never returns the device secret.
    ///
    /// Example request:
    /// <code>{"location":{"latitude":3.0738,"longitude":101.5183,"accuracyMeters":8.5,"timestamp":"2026-11-03T08:30:10Z"}}</code>
    ///
    /// Example success data contains challengeId, nonce, expiresAt, deviceUuid, and bleServiceUuid.
    /// </remarks>
    /// <response code="200">Challenge generated; the client can connect to the ESP32 and obtain a BLE proof.</response>
    /// <response code="401">No authenticated renter.</response>
    /// <response code="404">Booking not found or not owned by the renter.</response>
    /// <response code="409">Booking is invalid, outside its date/time window, or has no assigned device.</response>
    /// <response code="422">GPS accuracy or timestamp is invalid.</response>
    /// <response code="402">The renter has unpaid overstay penalties and insufficient wallet balance.</response>
    /// <response code="503">The backend could not activate BLE on the assigned ESP32.</response>
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status402PaymentRequired)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    [HttpPost("{bookingId:int}/unlock/challenge")]
    [HttpPost("~/api/bookings/{bookingId:int}/unlock/challenge")]
    [HttpPost("~/api/iot/{bookingId:int}/unlock/challenge")]
    public async Task<IActionResult> CreateUnlockChallenge(
        int bookingId,
        [FromBody] UnlockChallengeRequest request,
        CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        if (!userId.HasValue) return Unauthorized();

        var booking = await LoadOwnedBookingAsync(bookingId, userId.Value, cancellationToken);
        if (booking is null) return Error(StatusCodes.Status404NotFound, "BOOKING_NOT_FOUND", "Booking was not found.");

        var device = booking.ParkingSpot.IoTDevice;
        await LogDeviceActionAsync(device, "unlock_challenge_requested", "received", booking.BookingId);

        var eligibilityError = ValidateBookingAndDevice(booking, DateTimeOffset.UtcNow, requireAccessWindow: true);
        if (eligibilityError is not null)
        {
            await LogDeviceActionAsync(device, "unlock_challenge_eligibility", "rejected", booking.BookingId, DescribeResult(eligibilityError));
            return eligibilityError;
        }

        var locationError = ValidateLocation(
            request.Location,
            booking.ParkingSpot.Property,
            DateTimeOffset.UtcNow,
            out var distanceMeters);
        if (locationError is not null)
        {
            await LogDeviceActionAsync(device, "unlock_challenge_location", "rejected", booking.BookingId, DescribeResult(locationError));
            return locationError;
        }

        device = booking.ParkingSpot.IoTDevice!;
        var penaltySettlementError = await SettlePendingOverstayPenaltiesAsync(booking, device, cancellationToken);
        if (penaltySettlementError is not null) return penaltySettlementError;
        await LogDeviceActionAsync(device, "unlock_challenge_location", "verified", booking.BookingId,
            $"distanceMeters={distanceMeters!.Value.ToString("F1", CultureInfo.InvariantCulture)}");
        var challenge = _challenges.Create(booking.BookingId, userId.Value, device.IoTDeviceId, device.Esp32Serial);
        await LogDeviceActionAsync(device, "unlock_challenge_created", "success", booking.BookingId,
            $"challengeId={challenge.ChallengeId}; expiresAt={challenge.ExpiresAt:O}");
        try
        {
            // Ask only the assigned ESP32 to advertise for this challenge window.
            await _mqtt.PublishCommandAsync(
                device.Esp32Serial,
                $"BLE_START|{challenge.ChallengeId}|{challenge.Nonce}",
                cancellationToken);
            await LogDeviceActionAsync(device, "ble_advertising_activation", "published", booking.BookingId,
                $"challengeId={challenge.ChallengeId}");
        }
        catch (InvalidOperationException)
        {
            _challenges.Discard(challenge.ChallengeId);
            await LogDeviceActionAsync(device, "ble_advertising_activation", "failed", booking.BookingId,
                $"challengeId={challenge.ChallengeId}; MQTT unavailable");
            return Error(StatusCodes.Status503ServiceUnavailable, "BLE_DEVICE_UNAVAILABLE",
                "The bollard could not be activated over MQTT. Check that it is online and connected to the configured broker.");
        }

        return Ok(new
        {
            success = true,
            message = "Unlock verification challenge generated.",
            data = new
            {
                challengeId = challenge.ChallengeId,
                nonce = challenge.Nonce,
                expiresAt = challenge.ExpiresAt,
                deviceUuid = challenge.DeviceUuid,
                bleServiceUuid = _configuration["IoT:BleServiceUuid"] ?? DefaultBleServiceUuid,
                locationVerified = true,
                distanceMeters = Math.Round(distanceMeters!.Value, 1)
            }
        });
    }

    /// <summary>Requests a physical bollard lock for the booking.</summary>
    /// <remarks>
    /// The backend waits for the ESP32 acknowledgement before returning. The ESP32 reads the HC-SR04 immediately before moving the servo; a nearby obstacle or failed sensor reading is returned as a lock failure. This endpoint does not use cached telemetry as a safety decision. Locking remains available outside the booking access hours so a renter can secure the spot.
    /// </remarks>
    /// <response code="200">ESP32 confirmed that the bollard was locked.</response>
    /// <response code="409">Booking is not eligible, another identical command is in progress, or the ESP32 blocked locking because it detected an obstacle.</response>
    /// <response code="504">The command was published but no device acknowledgement arrived before timeout.</response>
    /// <response code="404">Booking not found or not owned by the renter.</response>
    /// <response code="503">MQTT is unavailable or the distance sensor failed.</response>
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status504GatewayTimeout)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    [HttpPost("~/api/iot/{bookingId:int}/lock")]
    public Task<IActionResult> LockBooking(
        int bookingId,
        CancellationToken cancellationToken)
        => SendRenterCommandAsync(bookingId, "LOCK", new BollardCommandRequest(), cancellationToken);

    /// <summary>Verifies GPS and BLE proof, then requests a bollard unlock.</summary>
    /// <remarks>
    /// Call the challenge endpoint first and obtain the HMAC proof from the assigned ESP32's BLE characteristic. Unlock is allowed only during the booking's Malaysia-time date, weekday, and time window. The backend validates booking ownership, GPS, challenge expiry and single use, BLE device UUID, and the cryptographic proof before publishing MQTT.
    ///
    /// Example request:
    /// <code>{"challengeId":"CHL-example","location":{"latitude":3.0738,"longitude":101.5183,"accuracyMeters":8.5,"timestamp":"2026-11-03T08:30:10Z"},"bleVerification":{"deviceUuid":"7d9f2c41-8a63-4b72-9e15-6c2a0f84b731","challengeResponse":"BASE64_HMAC_PROOF","rssi":-58}}</code>
    ///
    /// The backend waits for the matching ESP32 acknowledgement and returns only after the device confirms success or reports failure. If no acknowledgement arrives before timeout, execution status is unknown because the command may still have reached the bollard.
    /// </remarks>
    /// <response code="200">ESP32 confirmed that the bollard was unlocked.</response>
    /// <response code="409">Booking or challenge is invalid, another identical command is in progress, or the ESP32 blocked the command because it detected an obstacle.</response>
    /// <response code="403">GPS is out of range or BLE proof is invalid.</response>
    /// <response code="402">The renter has unpaid overstay penalties and insufficient wallet balance.</response>
    /// <response code="410">Challenge expired.</response>
    /// <response code="422">GPS accuracy or timestamp is invalid.</response>
    /// <response code="502">The device reported an execution failure.</response>
    /// <response code="503">MQTT is unavailable.</response>
    /// <response code="504">The command was published but no device acknowledgement arrived before timeout.</response>
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status402PaymentRequired)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status504GatewayTimeout)]
    [HttpPost("~/api/iot/{bookingId:int}/unlock")]
    public Task<IActionResult> UnlockBooking(
        int bookingId,
        [FromBody] BollardCommandRequest request,
        CancellationToken cancellationToken)
        => SendRenterCommandAsync(bookingId, "UNLOCK", request, cancellationToken);

    /// <summary>Requests a bollard lock as an administrator.</summary>
    /// <remarks>The backend waits for the ESP32 acknowledgement. The ESP32 performs its live HC-SR04 obstacle check before moving the servo.</remarks>
    /// <response code="200">ESP32 confirmed that the bollard was locked.</response>
    /// <response code="409">No assigned device is available, another identical command is in progress, or the ESP32 blocked locking because it detected an obstacle.</response>
    /// <response code="504">The command was published but no device acknowledgement arrived before timeout.</response>
    /// <response code="503">MQTT is unavailable or the distance sensor failed.</response>
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status504GatewayTimeout)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    [Authorize(Policy = "AdminOnly")]
    [HttpPost("~/api/admin/iot/{bookingId:int}/lock")]
    public Task<IActionResult> AdminLockBooking(int bookingId, CancellationToken cancellationToken)
        => SendAdminCommandAsync(bookingId, "LOCK", cancellationToken);

    /// <summary>Requests a bollard unlock as an administrator.</summary>
    /// <remarks>Admin unlock bypasses renter, GPS, BLE, and booking-window checks, but waits for the ESP32 acknowledgement before returning. Use only for operational recovery.</remarks>
    /// <response code="200">ESP32 confirmed that the bollard was unlocked.</response>
    /// <response code="409">No assigned device is available, another identical command is in progress, or the ESP32 rejected the command due to an obstacle.</response>
    /// <response code="504">The command was published but no device acknowledgement arrived before timeout.</response>
    /// <response code="503">MQTT is unavailable or the distance sensor failed.</response>
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [ProducesResponseType(StatusCodes.Status504GatewayTimeout)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    [Authorize(Policy = "AdminOnly")]
    [HttpPost("~/api/admin/iot/{bookingId:int}/unlock")]
    public Task<IActionResult> AdminUnlockBooking(int bookingId, CancellationToken cancellationToken)
        => SendAdminCommandAsync(bookingId, "UNLOCK", cancellationToken);

    private async Task<IActionResult> SendRenterCommandAsync(
        int bookingId,
        string command,
        BollardCommandRequest request,
        CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        if (!userId.HasValue) return Unauthorized();
        var booking = await LoadOwnedBookingAsync(bookingId, userId.Value, cancellationToken);
        if (booking is null) return Error(StatusCodes.Status404NotFound, "BOOKING_NOT_FOUND", "Booking was not found.");

        var device = booking.ParkingSpot.IoTDevice;
        await LogDeviceActionAsync(device, $"{command.ToLowerInvariant()}_requested", "received", booking.BookingId);

        var now = DateTimeOffset.UtcNow;
        var eligibilityError = ValidateBookingAndDevice(booking, now, requireAccessWindow: command == "UNLOCK");
        if (eligibilityError is not null)
        {
            await LogDeviceActionAsync(device, $"{command.ToLowerInvariant()}_eligibility", "rejected", booking.BookingId, DescribeResult(eligibilityError));
            return eligibilityError;
        }
        device = booking.ParkingSpot.IoTDevice!;

        double? distanceMeters = null;
        UnlockChallenge? verifiedChallenge = null;
        if (command == "UNLOCK")
        {
            var locationError = ValidateLocation(request.Location, booking.ParkingSpot.Property, now, out distanceMeters);
            if (locationError is not null)
            {
                await LogDeviceActionAsync(device, "unlock_location_check", "rejected", booking.BookingId, DescribeResult(locationError));
                return locationError;
            }
            await LogDeviceActionAsync(device, "unlock_location_check", "passed", booking.BookingId,
                $"distanceMeters={distanceMeters!.Value.ToString("F1", CultureInfo.InvariantCulture)}");

            var challengeId = request.ChallengeId?.Trim();
            if (string.IsNullOrWhiteSpace(challengeId))
            {
                await LogDeviceActionAsync(device, "unlock_challenge_validation", "rejected", booking.BookingId, "Challenge ID was missing.");
                return Error(StatusCodes.Status400BadRequest, "INVALID_CHALLENGE", "A challengeId is required for UNLOCK.");
            }

            var lookup = _challenges.Find(challengeId, now);
            if (lookup.State == ChallengeState.Expired)
            {
                await LogDeviceActionAsync(device, "unlock_challenge_validation", "expired", booking.BookingId, $"challengeId={challengeId}");
                return Error(StatusCodes.Status410Gone, "CHALLENGE_EXPIRED", "The unlock challenge has expired.");
            }
            if (lookup.State == ChallengeState.Used)
            {
                await LogDeviceActionAsync(device, "unlock_challenge_validation", "replay_rejected", booking.BookingId, $"challengeId={challengeId}");
                return Error(StatusCodes.Status409Conflict, "CHALLENGE_ALREADY_USED", "The unlock challenge has already been used.");
            }
            if (lookup.State != ChallengeState.Available || lookup.Challenge is null)
            {
                await LogDeviceActionAsync(device, "unlock_challenge_validation", "invalid", booking.BookingId, $"challengeId={challengeId}");
                return Error(StatusCodes.Status400BadRequest, "INVALID_CHALLENGE", "The unlock challenge is invalid.");
            }

            var challenge = lookup.Challenge;
            if (challenge.BookingId != booking.BookingId || challenge.RenterId != userId.Value ||
                challenge.DeviceId != device.IoTDeviceId || !string.Equals(challenge.DeviceUuid, device.Esp32Serial, StringComparison.OrdinalIgnoreCase))
            {
                await LogDeviceActionAsync(device, "unlock_challenge_validation", "binding_rejected", booking.BookingId, $"challengeId={challenge.ChallengeId}");
                return Error(StatusCodes.Status400BadRequest, "INVALID_CHALLENGE", "The challenge does not belong to this booking, renter, and device.");
            }

            var bleError = ValidateBleProof(request.BleVerification, challenge);
            if (bleError is not null)
            {
                await LogDeviceActionAsync(device, "ble_proof_verification", "failed", booking.BookingId, $"challengeId={challenge.ChallengeId}; {bleError}");
                return bleError;
            }
            await LogDeviceActionAsync(device, "ble_proof_verification", "passed", booking.BookingId, $"challengeId={challenge.ChallengeId}");

            var penaltySettlementError = await SettlePendingOverstayPenaltiesAsync(booking, device, cancellationToken);
            if (penaltySettlementError is not null) return penaltySettlementError;

            // The final atomic remove is the single-use gate for concurrent submissions.
            if (!_challenges.TryConsume(challenge.ChallengeId, DateTimeOffset.UtcNow))
            {
                var latest = _challenges.Find(challenge.ChallengeId, DateTimeOffset.UtcNow);
                await LogDeviceActionAsync(device, "unlock_challenge_consumption", "rejected", booking.BookingId, $"challengeId={challenge.ChallengeId}; state={latest.State}");
                return latest.State == ChallengeState.Expired
                    ? Error(StatusCodes.Status410Gone, "CHALLENGE_EXPIRED", "The unlock challenge has expired.")
                    : Error(StatusCodes.Status409Conflict, "CHALLENGE_ALREADY_USED", "The unlock challenge has already been used.");
            }

            verifiedChallenge = challenge;
            await LogDeviceActionAsync(device, "unlock_challenge_consumption", "consumed", booking.BookingId, $"challengeId={challenge.ChallengeId}");
        }

        MqttCommandResult commandResult;
        try
        {
            commandResult = await _mqtt.PublishCommandAndWaitForAckAsync(
                device.Esp32Serial, booking.BookingId, command, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            await LogDeviceActionAsync(device, $"mqtt_{command.ToLowerInvariant()}_command", "failed", booking.BookingId, "MQTT unavailable.");
            await _accessLogs.LogAsync(userId.Value, $"Bollard {command} command", false,
                "MQTT unavailable.", booking.BookingId, device.IoTDeviceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { success = false, errorCode = "MQTT_UNAVAILABLE", message = "The device control service is temporarily unavailable." });
        }

        var commandSucceeded = IsSuccessfulDeviceAck(commandResult, command);
        var resultDetails = commandResult.Message ?? commandResult.ErrorCode ?? commandResult.Result ?? commandResult.WaitStatus.ToString();
        await LogDeviceActionAsync(device, $"mqtt_{command.ToLowerInvariant()}_command",
            commandSucceeded ? "acknowledged_success" : commandResult.WaitStatus == MqttCommandWaitStatus.TimedOut ? "ack_timeout" : "acknowledged_failure",
            booking.BookingId,
            $"result={commandResult.Result ?? "none"}; errorCode={commandResult.ErrorCode ?? "none"}; message={resultDetails}; distanceCm={commandResult.DistanceCm?.ToString("F1", CultureInfo.InvariantCulture) ?? "unknown"}; challengeId={verifiedChallenge?.ChallengeId ?? "none"}");
        await _accessLogs.LogAsync(userId.Value, $"Bollard {command} command", commandSucceeded,
            command == "UNLOCK"
                ? $"GPS and BLE verified ({distanceMeters!.Value.ToString("F1", CultureInfo.InvariantCulture)} m); device result: {resultDetails}. Challenge {verifiedChallenge!.ChallengeId}."
                : $"Device result: {resultDetails}",
            booking.BookingId, device.IoTDeviceId);

        return CreateDeviceCommandResponse(commandResult, command, booking.BookingId, device.Esp32Serial,
            command == "UNLOCK", distanceMeters, isAdmin: false);
    }

    private async Task<IActionResult> SendAdminCommandAsync(
        int bookingId,
        string command,
        CancellationToken cancellationToken)
    {
        var booking = await LoadBookingWithDeviceAsync(bookingId, cancellationToken);
        if (booking is null)
            return Error(StatusCodes.Status404NotFound, "BOOKING_NOT_FOUND", "Booking was not found.");

        var device = booking.ParkingSpot.IoTDevice;
        if (device is null || !device.IsAssigned || string.IsNullOrWhiteSpace(device.Esp32Serial))
            return Error(StatusCodes.Status409Conflict, "DEVICE_NOT_ASSIGNED", "No assigned IoT device is available for this booking.");

        var adminId = _currentUser.UserId;
        await LogDeviceActionAsync(device, $"admin_{command.ToLowerInvariant()}_requested", "received", booking.BookingId,
            $"adminUserId={adminId?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}; validation override requested.");

        // The ESP32 performs the live HC-SR04 obstacle interlock immediately before moving the servo.

        MqttCommandResult commandResult;
        try
        {
            commandResult = await _mqtt.PublishCommandAndWaitForAckAsync(
                device.Esp32Serial, booking.BookingId, command, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            await LogDeviceActionAsync(device, $"admin_mqtt_{command.ToLowerInvariant()}_command", "failed", booking.BookingId,
                $"adminUserId={adminId?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}; MQTT unavailable.");
            await _accessLogs.LogAsync(adminId, $"Admin bollard {command} command", false,
                "MQTT unavailable.", booking.BookingId, device.IoTDeviceId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { success = false, errorCode = "MQTT_UNAVAILABLE", message = "The device control service is temporarily unavailable." });
        }

        var commandSucceeded = IsSuccessfulDeviceAck(commandResult, command);
        var resultDetails = commandResult.Message ?? commandResult.ErrorCode ?? commandResult.Result ?? commandResult.WaitStatus.ToString();
        await LogDeviceActionAsync(device, $"admin_mqtt_{command.ToLowerInvariant()}_command",
            commandSucceeded ? "acknowledged_success" : commandResult.WaitStatus == MqttCommandWaitStatus.TimedOut ? "ack_timeout" : "acknowledged_failure",
            booking.BookingId,
            $"adminUserId={adminId?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}; validationOverride=true; result={commandResult.Result ?? "none"}; errorCode={commandResult.ErrorCode ?? "none"}; message={resultDetails}");
        await _accessLogs.LogAsync(adminId, $"Admin bollard {command} command", commandSucceeded,
            $"Device result: {resultDetails}; validation override used.", booking.BookingId, device.IoTDeviceId);

        return CreateDeviceCommandResponse(commandResult, command, booking.BookingId, device.Esp32Serial,
            locationVerified: false, distanceMeters: null, isAdmin: true);
    }

    private Task<Booking?> LoadOwnedBookingAsync(int bookingId, int userId, CancellationToken cancellationToken)
        => _db.Bookings
            .Include(b => b.ParkingSpot)
                .ThenInclude(p => p.Property)
            .Include(b => b.ParkingSpot)
                .ThenInclude(p => p.IoTDevice)
            .Include(b => b.ParkingSpot)
                .ThenInclude(p => p.ParkingAvailabilities)
            .FirstOrDefaultAsync(b => b.BookingId == bookingId && b.RenterId == userId, cancellationToken);

    private static bool IsSuccessfulDeviceAck(MqttCommandResult result, string command)
    {
        var expectedPosition = command == "UNLOCK" ? "UNLOCKED" : "LOCKED";
        return result.WaitStatus == MqttCommandWaitStatus.Acknowledged &&
               string.Equals(result.Result, "SUCCESS", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(result.BollardPosition, expectedPosition, StringComparison.OrdinalIgnoreCase);
    }

    private IActionResult CreateDeviceCommandResponse(
        MqttCommandResult result,
        string command,
        int bookingId,
        string deviceUuid,
        bool locationVerified,
        double? distanceMeters,
        bool isAdmin)
    {
        if (result.WaitStatus == MqttCommandWaitStatus.TimedOut)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout, new
            {
                success = false,
                errorCode = "DEVICE_ACK_TIMEOUT",
                message = $"No response was received from the bollard for {command}. Its execution status is unknown; the command may still have reached the device.",
                data = new { bookingId, deviceUuid, command, executionStatus = "unknown" }
            });
        }

        if (result.WaitStatus == MqttCommandWaitStatus.Busy)
        {
            return Conflict(new
            {
                success = false,
                errorCode = "DEVICE_COMMAND_IN_PROGRESS",
                message = result.Message ?? "A matching command is already awaiting the bollard response.",
                data = new { bookingId, deviceUuid, command, executionStatus = "in_progress" }
            });
        }

        if (IsSuccessfulDeviceAck(result, command))
        {
            return Ok(new
            {
                success = true,
                message = $"Bollard {command.ToLowerInvariant()} confirmed by the device.",
                data = new
                {
                    bookingId,
                    deviceUuid,
                    command,
                    commandId = result.CommandId,
                    locationVerified = locationVerified ? true : (bool?)null,
                    bleVerified = locationVerified ? true : (bool?)null,
                    distanceMeters = locationVerified ? Math.Round(distanceMeters ?? 0, 1) : (double?)null,
                    validationOverride = isAdmin ? true : (bool?)null,
                    executionStatus = "confirmed_success",
                    deviceResult = result.Result,
                    deviceMessage = result.Message,
                    distanceCm = result.DistanceCm,
                    bollardPosition = result.BollardPosition
                }
            });
        }

        var resultCode = result.Result?.Trim().ToUpperInvariant();
        var expectedPosition = command == "UNLOCK" ? "UNLOCKED" : "LOCKED";
        var deviceMessage = result.Message ?? result.ErrorCode ?? (resultCode switch
        {
            "OBSTACLE_DETECTED" => "Obstacle detected within 50 cm. The servo was not moved.",
            "SENSOR_READING_UNAVAILABLE" => "The HC-SR04 could not read a distance, so the servo was not moved.",
            "INVALID_COMMAND" => "The ESP32 rejected the command as invalid.",
            "SUCCESS" => $"The device reported position '{result.BollardPosition ?? "unknown"}'; expected '{expectedPosition}'.",
            _ => result.Result ?? "The bollard reported an unspecified failure."
        });
        var obstacleDetected = string.Equals(resultCode, "OBSTACLE_DETECTED", StringComparison.OrdinalIgnoreCase) ||
                               $"{result.ErrorCode} {deviceMessage}".Contains("obstacle", StringComparison.OrdinalIgnoreCase);
        var sensorFailure = string.Equals(resultCode, "SENSOR_READING_UNAVAILABLE", StringComparison.OrdinalIgnoreCase) ||
                            $"{result.ErrorCode} {deviceMessage}".Contains("sensor", StringComparison.OrdinalIgnoreCase) ||
                            $"{result.ErrorCode} {deviceMessage}".Contains("hc-sr04", StringComparison.OrdinalIgnoreCase);
        var errorCode = obstacleDetected ? "OBSTACLE_DETECTED" : sensorFailure ? "DISTANCE_SENSOR_FAILED" : "DEVICE_COMMAND_FAILED";
        var httpStatus = obstacleDetected ? StatusCodes.Status409Conflict : sensorFailure
            ? StatusCodes.Status503ServiceUnavailable
            : StatusCodes.Status502BadGateway;
        return StatusCode(httpStatus, new
        {
            success = false,
            errorCode,
            message = $"Bollard {command.ToLowerInvariant()} failed: {deviceMessage}",
            data = new
            {
                bookingId,
                deviceUuid,
                command,
                commandId = result.CommandId,
                executionStatus = "confirmed_failure",
                deviceResult = result.Result,
                deviceErrorCode = result.ErrorCode,
                deviceMessage,
                distanceCm = result.DistanceCm,
                bollardPosition = result.BollardPosition,
                validationOverride = isAdmin ? true : (bool?)null
            }
        });
    }

    private Task<Booking?> LoadBookingWithDeviceAsync(int bookingId, CancellationToken cancellationToken)
        => _db.Bookings
            .Include(b => b.ParkingSpot)
                .ThenInclude(p => p.IoTDevice)
            .FirstOrDefaultAsync(b => b.BookingId == bookingId, cancellationToken);

    private async Task<IActionResult?> SettlePendingOverstayPenaltiesAsync(
        Booking booking,
        IoTDevice device,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var renterWallet = await _db.Wallets
            // WalletStatus is configured as a string in ApplicationDbContext.
            .FromSqlInterpolated($"SELECT * FROM [Wallets] WITH (UPDLOCK, HOLDLOCK) WHERE [UserId] = {booking.RenterId} AND [Status] = {nameof(WalletStatus.Active)}")
            .FirstOrDefaultAsync(cancellationToken);
        var pending = await _db.Transactions
            .Where(t => t.BookingId != null && t.TransactionType == TransactionType.OverstayPenalty &&
                        t.TransactionStatus == TransactionStatus.Pending && t.Amount < 0 &&
                        t.Booking!.RenterId == booking.RenterId)
            .Include(t => t.Booking!)
                .ThenInclude(b => b.ParkingSpot)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
        if (pending.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }
        var due = pending.Sum(t => -t.Amount);
        if (renterWallet is null || renterWallet.Balance < due)
        {
            await transaction.RollbackAsync(cancellationToken);
            await LogDeviceActionAsync(device, "overstay_penalty_settlement", "insufficient_balance", booking.BookingId,
                $"due=RM{due:F2}; balance=RM{renterWallet?.Balance ?? 0:F2}");
            return new ObjectResult(new
            {
                success = false,
                errorCode = "PENALTY_DUE",
                message = "Unpaid overstay penalties must be paid before the next bollard unlock. Please top up your wallet.",
                data = new { dueAmount = due, walletBalance = renterWallet?.Balance ?? 0m }
            }) { StatusCode = StatusCodes.Status402PaymentRequired };
        }

        var ownerIds = pending.Select(t => t.Booking!.ParkingSpot.OwnerId).Distinct().ToList();
        var ownerWallets = await _db.Wallets
            .Where(w => ownerIds.Contains(w.UserId) && w.Status == WalletStatus.Active)
            .ToDictionaryAsync(w => w.UserId, cancellationToken);
        if (pending.Any(t => !ownerWallets.ContainsKey(t.Booking!.ParkingSpot.OwnerId)))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Error(StatusCodes.Status503ServiceUnavailable, "PENALTY_SETTLEMENT_UNAVAILABLE",
                "The penalty payment cannot be settled right now. Please try again later.");
        }

        var now = DateTime.UtcNow;
        renterWallet.Balance -= due;
        renterWallet.UpdatedAt = now;
        foreach (var item in pending)
        {
            var fee = -item.Amount;
            var ownerWallet = ownerWallets[item.Booking!.ParkingSpot.OwnerId];
            ownerWallet.Balance += fee;
            ownerWallet.UpdatedAt = now;
            item.TransactionStatus = TransactionStatus.Completed;
            item.UpdatedAt = now;
            _db.Transactions.Add(new Transaction
            {
                WalletId = ownerWallet.WalletId,
                BookingId = item.BookingId,
                TransactionType = TransactionType.OverstayPenalty,
                Amount = fee,
                PaymentMethod = PaymentMethod.Wallet,
                TransactionStatus = TransactionStatus.Completed,
                ReferenceNumber = $"SETTLE-{item.ReferenceNumber}",
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        IoTDeviceLogWriter.Add(_db, device, "overstay_penalty_settlement", "completed", now, booking.BookingId,
            $"settledAmount=RM{due:F2}; pendingTransactions={pending.Count}");
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return null;
    }

    private async Task LogDeviceActionAsync(
        IoTDevice? device,
        string action,
        string result,
        int? bookingId = null,
        string? details = null)
    {
        if (device is null) return;

        var now = DateTime.UtcNow;
        try
        {
            IoTDeviceLogWriter.Add(_db, device, action, result, now, bookingId, details);
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not persist IoT device log action {Action} for device {DeviceUuid}", action, device.Esp32Serial);
        }
    }

    private static string DescribeResult(IActionResult result)
        => result is ObjectResult objectResult && objectResult.Value is not null
            ? JsonSerializer.Serialize(objectResult.Value)
            : result.GetType().Name;

    private IActionResult? ValidateBookingAndDevice(Booking booking, DateTimeOffset now, bool requireAccessWindow)
    {
        if (booking.BookingStatus is not (BookingStatus.Confirmed or BookingStatus.Active))
            return Error(StatusCodes.Status409Conflict, "BOOKING_NOT_ELIGIBLE", "The booking is not eligible for bollard control.");

        var device = booking.ParkingSpot.IoTDevice;
        if (device is null || !device.IsAssigned || string.IsNullOrWhiteSpace(device.Esp32Serial))
            return Error(StatusCodes.Status409Conflict, "DEVICE_NOT_ASSIGNED", "No assigned IoT device is available for this booking.");

        if (requireAccessWindow)
        {
            // Booking dates and availability rules are calendar values in Malaysia time.
            // Do not label the stored midnight date boundaries as UTC; doing so shifts them by eight hours.
            var malaysiaNow = now.ToOffset(TimeSpan.FromHours(8));
            var localDate = DateOnly.FromDateTime(malaysiaNow.DateTime);
            var bookingStartDate = DateOnly.FromDateTime(booking.StartDate);
            var bookingEndDateExclusive = DateOnly.FromDateTime(booking.EndDate);

            if (localDate < bookingStartDate || localDate >= bookingEndDateExclusive)
                return Error(StatusCodes.Status409Conflict, "BOOKING_NOT_ELIGIBLE", "The current Malaysia date is outside the booking dates.");

            var localTime = TimeOnly.FromDateTime(malaysiaNow.DateTime);
            if (!IsWithinAvailabilityWindow(booking.ParkingSpot.ParkingAvailabilities, localDate, localTime))
                return Error(StatusCodes.Status409Conflict, "BOOKING_ACCESS_WINDOW_CLOSED", "The bollard can only be unlocked during the parking spot's scheduled days and hours in Malaysia time.");
        }

        return null;
    }

    private static bool IsWithinAvailabilityWindow(
        IEnumerable<Availability> rules,
        DateOnly localDate,
        TimeOnly localTime)
    {
        return rules.Any(rule =>
            (!rule.EffectiveFrom.HasValue || localDate >= rule.EffectiveFrom.Value) &&
            (!rule.EffectiveUntil.HasValue || localDate <= rule.EffectiveUntil.Value) &&
            (rule.DayType == DayType.Everyday ||
             (rule.DayType == DayType.Weekday && localDate.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) ||
             (rule.DayType == DayType.Weekend && localDate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)) &&
            localTime >= rule.StartTime &&
            (localTime < rule.EndTime ||
             // Owner availability rules use HH:mm and cannot represent 24:00. Treat 00:00–23:59
             // as an all-day window through the final minute; a matching rule on the next day
             // keeps overnight stays valid for 24-hour listings.
             (rule.StartTime == TimeOnly.MinValue && rule.EndTime == new TimeOnly(23, 59) && localTime.Hour == 23 && localTime.Minute == 59)));
    }

    private IActionResult? ValidateLocation(UnlockLocation? location, Property property, DateTimeOffset now, out double? distanceMeters)
    {
        distanceMeters = null;
        if (location is null || !location.Latitude.HasValue || !location.Longitude.HasValue || !location.AccuracyMeters.HasValue || !location.Timestamp.HasValue)
            return Error(StatusCodes.Status400BadRequest, "LOCATION_UNAVAILABLE", "GPS coordinates, accuracy, and timestamp are required.");

        var lat = location.Latitude.Value;
        var lon = location.Longitude.Value;
        var accuracy = location.AccuracyMeters.Value;
        if (lat is < -90 or > 90 || lon is < -180 or > 180 || accuracy < 0 || !double.IsFinite(lat) || !double.IsFinite(lon) || !double.IsFinite(accuracy))
            return Error(StatusCodes.Status400BadRequest, "LOCATION_UNAVAILABLE", "The submitted GPS location is invalid.");

        var maxAccuracy = ReadPositiveDouble("IoT:Unlock:MaximumLocationAccuracyMeters", 100);
        if (accuracy > maxAccuracy)
            return Error(StatusCodes.Status422UnprocessableEntity, "LOCATION_INACCURATE", $"GPS accuracy must be {maxAccuracy.ToString("F0", CultureInfo.InvariantCulture)} metres or better.");

        var locationTime = location.Timestamp.Value.ToUniversalTime();
        var maxAge = TimeSpan.FromSeconds(ReadPositiveDouble("IoT:Unlock:MaximumLocationAgeSeconds", 30));
        var futureSkew = TimeSpan.FromSeconds(ReadPositiveDouble("IoT:Unlock:MaximumFutureClockSkewSeconds", 10));
        if (locationTime < now.Subtract(maxAge) || locationTime > now.Add(futureSkew))
            return Error(StatusCodes.Status422UnprocessableEntity, "LOCATION_UNAVAILABLE", "The GPS timestamp is stale or too far in the future.");

        distanceMeters = HaversineMeters(lat, lon, (double)property.Latitude, (double)property.Longitude);
        if (distanceMeters > 100)
            return Error(StatusCodes.Status403Forbidden, "LOCATION_OUT_OF_RANGE", "The submitted location is more than 100 metres from the property.");

        return null;
    }

    private IActionResult? ValidateBleProof(BleVerification? proof, UnlockChallenge challenge)
    {
        if (proof is null || string.IsNullOrWhiteSpace(proof.DeviceUuid) || string.IsNullOrWhiteSpace(proof.ChallengeResponse))
            return Error(StatusCodes.Status400BadRequest, "BLE_DEVICE_NOT_FOUND", "A BLE device UUID and challenge response are required.");

        if (!string.Equals(proof.DeviceUuid, challenge.DeviceUuid, StringComparison.OrdinalIgnoreCase))
            return Error(StatusCodes.Status403Forbidden, "BLE_DEVICE_NOT_FOUND", "The BLE proof came from a different device.");

        var secret = _configuration[$"IoT:DeviceSecrets:{challenge.DeviceUuid}"];
        if (string.IsNullOrWhiteSpace(secret))
            return Error(StatusCodes.Status503ServiceUnavailable, "DEVICE_AUTHENTICATION_FAILED", "Device authentication is not configured on the backend.");

        byte[] supplied;
        try { supplied = Convert.FromBase64String(proof.ChallengeResponse); }
        catch (FormatException)
        {
            return Error(StatusCodes.Status403Forbidden, "BLE_VERIFICATION_FAILED", "The BLE challenge response is invalid.");
        }

        var message = challenge.Nonce + challenge.DeviceUuid.ToLowerInvariant() + challenge.ChallengeId;
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(message));
        return supplied.Length == expected.Length && CryptographicOperations.FixedTimeEquals(supplied, expected)
            ? null
            : Error(StatusCodes.Status403Forbidden, "BLE_VERIFICATION_FAILED", "The BLE challenge response could not be verified.");
    }

    private static double HaversineMeters(double latitude1, double longitude1, double latitude2, double longitude2)
    {
        const double earthRadiusMeters = 6_371_000;
        static double ToRadians(double degrees) => degrees * Math.PI / 180;
        var dLat = ToRadians(latitude2 - latitude1);
        var dLon = ToRadians(longitude2 - longitude1);
        var a = Math.Pow(Math.Sin(dLat / 2), 2) +
                Math.Cos(ToRadians(latitude1)) * Math.Cos(ToRadians(latitude2)) * Math.Pow(Math.Sin(dLon / 2), 2);
        a = Math.Clamp(a, 0, 1);
        return earthRadiusMeters * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private double ReadPositiveDouble(string key, double fallback)
        => double.TryParse(_configuration[key], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : fallback;

    private static IActionResult Error(int statusCode, string errorCode, string message)
        => new ObjectResult(new { success = false, errorCode, message }) { StatusCode = statusCode };
}

public sealed class BollardCommandRequest
{
    // Omitted command means UNLOCK on /api/bookings/{id}/unlock; existing /command callers
    // should continue to send LOCK or UNLOCK.
    /// <summary>Optional command for legacy /command callers. The dedicated booking unlock route defaults to UNLOCK.</summary>
    public string? Command { get; set; }

    /// <summary>Single-use challenge identifier returned by the unlock challenge endpoint.</summary>
    public string? ChallengeId { get; set; }

    /// <summary>Fresh client GPS coordinates and timestamp.</summary>
    public UnlockLocation? Location { get; set; }

    /// <summary>HMAC proof returned by the assigned ESP32 over BLE. Never contains the device secret.</summary>
    public BleVerification? BleVerification { get; set; }
}

public sealed class UnlockLocation
{
    /// <summary>Latitude in decimal degrees.</summary>
    public double? Latitude { get; set; }

    /// <summary>Longitude in decimal degrees.</summary>
    public double? Longitude { get; set; }

    /// <summary>Client-reported GPS accuracy in metres.</summary>
    public double? AccuracyMeters { get; set; }

    /// <summary>GPS reading timestamp as an ISO 8601 value with a UTC offset.</summary>
    public DateTimeOffset? Timestamp { get; set; }
}

public sealed class BleVerification
{
    /// <summary>UUID of the assigned ESP32 that generated the BLE proof.</summary>
    public string? DeviceUuid { get; set; }

    /// <summary>Base64-encoded HMAC-SHA256 response produced by the ESP32.</summary>
    public string? ChallengeResponse { get; set; }

    /// <summary>Optional BLE signal strength in dBm. Informational only; it does not prove physical distance.</summary>
    public int? Rssi { get; set; }
}

public sealed class UnlockChallengeRequest
{
    /// <summary>Fresh GPS reading used to verify location before generating a challenge.</summary>
    public UnlockLocation? Location { get; set; }
}
