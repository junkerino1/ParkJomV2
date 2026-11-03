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

    [HttpPost("~/api/iot/{bookingId:int}/lock")]
    public Task<IActionResult> LockBooking(
        int bookingId,
        CancellationToken cancellationToken)
        => SendRenterCommandAsync(bookingId, "LOCK", new BollardCommandRequest(), cancellationToken);

    [HttpPost("~/api/iot/{bookingId:int}/unlock")]
    public Task<IActionResult> UnlockBooking(
        int bookingId,
        [FromBody] BollardCommandRequest request,
        CancellationToken cancellationToken)
        => SendRenterCommandAsync(bookingId, "UNLOCK", request, cancellationToken);

    [Authorize(Policy = "AdminOnly")]
    [HttpPost("~/api/admin/iot/{bookingId:int}/lock")]
    public Task<IActionResult> AdminLockBooking(int bookingId, CancellationToken cancellationToken)
        => SendAdminCommandAsync(bookingId, "LOCK", cancellationToken);

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

        if (command == "LOCK")
        {
            var lockSafetyError = await ValidateLockClearanceAsync(device.IoTDeviceId, cancellationToken);
            if (lockSafetyError is not null)
            {
                await LogDeviceActionAsync(device, "manual_lock_sensor_check", "rejected", booking.BookingId, DescribeResult(lockSafetyError));
                return lockSafetyError;
            }
            await LogDeviceActionAsync(device, "manual_lock_sensor_check", "passed", booking.BookingId, "Latest HC-SR04 reading was greater than 50 cm.");
        }

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

        try
        {
            await _mqtt.PublishCommandAsync(device.Esp32Serial, $"{command}|{booking.BookingId}", cancellationToken);
            await LogDeviceActionAsync(device, $"mqtt_{command.ToLowerInvariant()}_command", "published", booking.BookingId,
                command == "UNLOCK"
                    ? $"GPS and BLE verified; distanceMeters={distanceMeters!.Value.ToString("F1", CultureInfo.InvariantCulture)}; challengeId={verifiedChallenge!.ChallengeId}"
                    : $"deviceUuid={device.Esp32Serial}");
            await _accessLogs.LogAsync(userId.Value, $"Bollard {command} command", true,
                command == "UNLOCK"
                    ? $"GPS and BLE verified ({distanceMeters!.Value.ToString("F1", CultureInfo.InvariantCulture)} m); MQTT command published. Challenge {verifiedChallenge!.ChallengeId}."
                    : $"Published for device {device.Esp32Serial}",
                booking.BookingId, device.IoTDeviceId);
        }
        catch (InvalidOperationException)
        {
            await LogDeviceActionAsync(device, $"mqtt_{command.ToLowerInvariant()}_command", "failed", booking.BookingId, "MQTT unavailable.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { success = false, errorCode = "MQTT_UNAVAILABLE", message = "The device control service is temporarily unavailable." });
        }

        return Accepted(new
        {
            success = true,
            message = command == "UNLOCK" ? "Location verified. Bollard unlock command sent." : "Bollard lock command sent.",
            data = command == "UNLOCK"
                ? (object)new
                {
                    bookingId = booking.BookingId,
                    deviceUuid = device.Esp32Serial,
                    locationVerified = true,
                    bleVerified = true,
                    distanceMeters = Math.Round(distanceMeters!.Value, 1),
                    unlockCommandSent = true,
                    executionStatus = "awaiting_device_ack"
                }
                : new { bookingId = booking.BookingId, deviceUuid = device.Esp32Serial, command, executionStatus = "awaiting_device_ack" }
        });
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

        // Admin commands bypass renter eligibility and GPS/BLE checks, but the physical lock
        // obstacle interlock remains mandatory for every LOCK request.
        if (command == "LOCK")
        {
            var lockSafetyError = await ValidateLockClearanceAsync(device.IoTDeviceId, cancellationToken);
            if (lockSafetyError is not null)
            {
                await LogDeviceActionAsync(device, "admin_lock_sensor_check", "rejected", booking.BookingId,
                    DescribeResult(lockSafetyError));
                return lockSafetyError;
            }
            await LogDeviceActionAsync(device, "admin_lock_sensor_check", "passed", booking.BookingId,
                "Latest HC-SR04 reading was greater than 50 cm.");
        }

        try
        {
            await _mqtt.PublishCommandAsync(device.Esp32Serial, $"{command}|{booking.BookingId}", cancellationToken);
            await LogDeviceActionAsync(device, $"admin_mqtt_{command.ToLowerInvariant()}_command", "published", booking.BookingId,
                $"adminUserId={adminId?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}; renter eligibility and GPS/BLE checks bypassed.");
            await _accessLogs.LogAsync(adminId, $"Admin bollard {command} command", true,
                $"Published for device {device.Esp32Serial}; validation override used.", booking.BookingId, device.IoTDeviceId);
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

        return Accepted(new
        {
            success = true,
            message = $"Admin bollard {command.ToLowerInvariant()} command sent.",
            data = new
            {
                bookingId = booking.BookingId,
                deviceUuid = device.Esp32Serial,
                command,
                validationOverride = true,
                executionStatus = "awaiting_device_ack"
            }
        });
    }

    private Task<Booking?> LoadOwnedBookingAsync(int bookingId, int userId, CancellationToken cancellationToken)
        => _db.Bookings
            .Include(b => b.ParkingSpot)
                .ThenInclude(p => p.Property)
            .Include(b => b.ParkingSpot)
                .ThenInclude(p => p.IoTDevice)
            .FirstOrDefaultAsync(b => b.BookingId == bookingId && b.RenterId == userId, cancellationToken);

    private Task<Booking?> LoadBookingWithDeviceAsync(int bookingId, CancellationToken cancellationToken)
        => _db.Bookings
            .Include(b => b.ParkingSpot)
                .ThenInclude(p => p.IoTDevice)
            .FirstOrDefaultAsync(b => b.BookingId == bookingId, cancellationToken);

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

    private async Task<IActionResult?> ValidateLockClearanceAsync(int deviceId, CancellationToken cancellationToken)
    {
        var latest = await _db.IoTStatusLogs
            .AsNoTracking()
            .Where(log => log.IoTDeviceId == deviceId && log.Topic != null && log.Topic.EndsWith("/telemetry"))
            .OrderByDescending(log => log.LoggedAt)
            .Select(log => new { log.Message, log.LoggedAt })
            .FirstOrDefaultAsync(cancellationToken);

        var readingAge = latest is null ? TimeSpan.MaxValue : DateTime.UtcNow - latest.LoggedAt;
        if (latest is null || readingAge < TimeSpan.Zero || readingAge > TimeSpan.FromMinutes(6) || string.IsNullOrWhiteSpace(latest.Message))
            return Error(StatusCodes.Status409Conflict, "SENSOR_READING_UNAVAILABLE",
                "A fresh HC-SR04 reading is not available. The bollard was not locked.");

        try
        {
            using var document = JsonDocument.Parse(latest.Message);
            if (!document.RootElement.TryGetProperty("distanceCm", out var distanceElement) ||
                distanceElement.ValueKind != JsonValueKind.Number || !distanceElement.TryGetDouble(out var distanceCm) ||
                !double.IsFinite(distanceCm))
            {
                return Error(StatusCodes.Status409Conflict, "SENSOR_READING_UNAVAILABLE",
                    "The HC-SR04 could not confirm a clear path. The bollard was not locked.");
            }

            if (distanceCm <= 50)
            {
                return new ObjectResult(new
                {
                    success = false,
                    errorCode = "OBSTACLE_DETECTED",
                    message = "Obstacle detected within 50 cm. Move it clear before locking the bollard.",
                    data = new { distanceCm = Math.Round(distanceCm, 1), thresholdCm = 50 }
                })
                {
                    StatusCode = StatusCodes.Status409Conflict
                };
            }
        }
        catch (JsonException)
        {
            return Error(StatusCodes.Status409Conflict, "SENSOR_READING_UNAVAILABLE",
                "The latest HC-SR04 reading could not be read. The bollard was not locked.");
        }

        return null;
    }

    private IActionResult? ValidateBookingAndDevice(Booking booking, DateTimeOffset now, bool requireAccessWindow)
    {
        if (booking.BookingStatus is not (BookingStatus.Confirmed or BookingStatus.Active))
            return Error(StatusCodes.Status409Conflict, "BOOKING_NOT_ELIGIBLE", "The booking is not eligible for bollard control.");

        var device = booking.ParkingSpot.IoTDevice;
        if (device is null || !device.IsAssigned || string.IsNullOrWhiteSpace(device.Esp32Serial))
            return Error(StatusCodes.Status409Conflict, "DEVICE_NOT_ASSIGNED", "No assigned IoT device is available for this booking.");

        if (requireAccessWindow && (now.UtcDateTime < AsUtc(booking.StartDate) || now.UtcDateTime > AsUtc(booking.EndDate)))
            return Error(StatusCodes.Status409Conflict, "BOOKING_NOT_ELIGIBLE", "The booking is outside its bollard access window.");

        return null;
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

    private static DateTime AsUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

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
    public string? Command { get; set; }
    public string? ChallengeId { get; set; }
    public UnlockLocation? Location { get; set; }
    public BleVerification? BleVerification { get; set; }
}

public sealed class UnlockLocation
{
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? AccuracyMeters { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
}

public sealed class BleVerification
{
    public string? DeviceUuid { get; set; }
    public string? ChallengeResponse { get; set; }
    public int? Rssi { get; set; }
}

public sealed class UnlockChallengeRequest
{
    public UnlockLocation? Location { get; set; }
}
