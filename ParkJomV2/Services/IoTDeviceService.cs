using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.Services;

/// <summary>Owns device registration, one-time binding codes, and atomic owner binding.</summary>
public sealed class IoTDeviceService
{
    private static readonly TimeSpan OtpLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan OtpCooldown = TimeSpan.FromSeconds(30);
    private const int HeartbeatOfflineAfterSeconds = 90;

    private readonly ApplicationDbContext _db;
    private readonly byte[] _otpPepper;
    private readonly string _mqttTopicRoot;

    public IoTDeviceService(ApplicationDbContext db, IConfiguration configuration)
    {
        _db = db;
        _mqttTopicRoot = (configuration["Mqtt:TopicRoot"] ?? "parkjom/devices").TrimEnd('/');
        var pepper = configuration["IoT:OtpPepper"] ?? configuration["JwtSettings:SecretKey"];
        if (string.IsNullOrWhiteSpace(pepper))
        {
            throw new InvalidOperationException("Configure IoT:OtpPepper before using device binding OTPs.");
        }

        _otpPepper = Encoding.UTF8.GetBytes(pepper);
    }

    public Task<List<IoTDeviceListItem>> ListForOwnerAsync(int ownerId, CancellationToken cancellationToken)
        => ListDevicesAsync(
            _db.IoTDevices.Where(d => d.ParkingSpot != null && d.ParkingSpot.OwnerId == ownerId),
            cancellationToken);

    public Task<List<IoTDeviceListItem>> ListAllAsync(CancellationToken cancellationToken)
        => ListDevicesAsync(_db.IoTDevices, cancellationToken);

    public async Task<IoTHeartbeatStatusResponse> GetHeartbeatStatusAsync(
        string uuid, int? ownerId, CancellationToken cancellationToken)
    {
        var device = await StatusDeviceQuery(uuid, ownerId).Select(d => new
            {
                d.IoTDeviceId,
                d.Esp32Serial,
                d.LastHeartbeatAt,
                ParkingSpot = d.ParkingSpot == null ? null : new IoTStatusParkingSpotResponse(
                    d.ParkingSpot.ParkingSpotId,
                    d.ParkingSpot.ParkingLabel,
                    d.ParkingSpot.PropertyId,
                    d.ParkingSpot.Property.PropertyName)
            })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new IoTDeviceOperationException(StatusCodes.Status404NotFound, "Device not found.");

        var lastScan = await GetLastDistanceScanAsync(device.IoTDeviceId, device.Esp32Serial, cancellationToken);
        var checkedAt = DateTimeOffset.UtcNow;
        DateTimeOffset? lastHeartbeatAt = device.LastHeartbeatAt is DateTime lastSeen
            ? new DateTimeOffset(DateTime.SpecifyKind(lastSeen, DateTimeKind.Utc))
            : null;
        var status = lastHeartbeatAt is not DateTimeOffset receivedAt
            ? "unknown"
            : checkedAt - receivedAt < TimeSpan.FromSeconds(HeartbeatOfflineAfterSeconds) ? "online" : "offline";

        return new IoTHeartbeatStatusResponse(
            device.Esp32Serial, status, lastHeartbeatAt, checkedAt, HeartbeatOfflineAfterSeconds,
            lastScan, device.ParkingSpot);
    }

    public async Task<IoTLiveStatusScanResponse> RequestLiveStatusScanAsync(
        string uuid, int? ownerId, IMqttService mqtt, CancellationToken cancellationToken)
    {
        var (deviceUuid, response) = await ReadLiveStatusAsync(uuid, ownerId, mqtt, cancellationToken);
        if (!string.Equals(response.Result, "SUCCESS", StringComparison.OrdinalIgnoreCase) ||
            response.DistanceCm is not double distance || distance < 0 ||
            response.ReceivedAt is not DateTimeOffset receivedAt)
        {
            throw new IoTDeviceOperationException(StatusCodes.Status502BadGateway, "The bollard did not return a valid distance reading.");
        }

        return new IoTLiveStatusScanResponse(deviceUuid, distance, receivedAt, response.BollardPosition);
    }

    public async Task<IoTLivePositionResponse> RequestLivePositionAsync(
        string uuid, int ownerId, IMqttService mqtt, CancellationToken cancellationToken)
    {
        var (_, response) = await ReadLiveStatusAsync(uuid, ownerId, mqtt, cancellationToken);
        var position = response.BollardPosition?.ToUpperInvariant();
        if (response.ReceivedAt is null || position is not ("LOCKED" or "UNLOCKED") ||
            response.Result?.ToUpperInvariant() is not ("SUCCESS" or "SENSOR_READING_UNAVAILABLE"))
        {
            throw new IoTDeviceOperationException(StatusCodes.Status502BadGateway, "The bollard did not report its position.");
        }
        return new IoTLivePositionResponse(position);
    }

    private async Task<(string DeviceUuid, MqttCommandResult Response)> ReadLiveStatusAsync(
        string uuid, int? ownerId, IMqttService mqtt, CancellationToken cancellationToken)
    {
        var deviceUuid = await StatusDeviceQuery(uuid, ownerId).Select(device => device.Esp32Serial)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new IoTDeviceOperationException(StatusCodes.Status404NotFound, "Device not found.");

        MqttCommandResult response;
        try
        {
            response = await mqtt.RequestStatusScanAsync(deviceUuid, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            throw new IoTDeviceOperationException(StatusCodes.Status503ServiceUnavailable,
                "The device status request could not be sent.", ex);
        }

        if (response.WaitStatus == MqttCommandWaitStatus.Busy)
            throw new IoTDeviceOperationException(StatusCodes.Status409Conflict, "A status scan is already in progress.");

        if (response.WaitStatus == MqttCommandWaitStatus.TimedOut)
            throw new IoTDeviceOperationException(StatusCodes.Status504GatewayTimeout, "The bollard did not reply to the status scan.");

        return (deviceUuid, response);
    }

    private IQueryable<IoTDevice> StatusDeviceQuery(string uuid, int? ownerId)
    {
        var normalizedUuid = NormalizeUuid(uuid)
            ?? throw new IoTDeviceOperationException(StatusCodes.Status400BadRequest, "A valid device UUID is required.");

        var query = _db.IoTDevices.AsNoTracking().Where(d => d.Esp32Serial == normalizedUuid);
        return ownerId is int id
            ? query.Where(d => d.ParkingSpot != null && d.ParkingSpot.OwnerId == id)
            : query;
    }

    private async Task<IoTDistanceScanResponse?> GetLastDistanceScanAsync(
        int deviceId, string deviceUuid, CancellationToken cancellationToken)
    {
        var topic = $"{_mqttTopicRoot}/{deviceUuid}/telemetry";
        var readings = _db.IoTStatusLogs.AsNoTracking()
            .Where(log => log.IoTDeviceId == deviceId && log.Topic == topic)
            .OrderByDescending(log => log.LoggedAt)
            .ThenByDescending(log => log.IoTStatusLogId)
            .Select(log => new { log.Message, log.LoggedAt });

        await foreach (var reading in readings.AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            if (MqttService.TryReadTelemetry(reading.Message, out var distanceCm, out _) &&
                distanceCm is double distance && distance >= 0)
            {
                var receivedAt = new DateTimeOffset(DateTime.SpecifyKind(reading.LoggedAt, DateTimeKind.Utc));
                return new IoTDistanceScanResponse(distance, receivedAt);
            }
        }

        return null;
    }

    private static Task<List<IoTDeviceListItem>> ListDevicesAsync(
        IQueryable<IoTDevice> devices,
        CancellationToken cancellationToken)
        => devices.AsNoTracking()
            .OrderBy(d => d.IoTDeviceId)
            .Select(d => new IoTDeviceListItem
            {
                IoTDeviceId = d.IoTDeviceId,
                DeviceUuid = d.Esp32Serial,
                DeviceStatus = d.DeviceStatus.ToString(),
                FirmwareVersion = d.FirmwareVersion,
                IsAssigned = d.IsAssigned,
                ParkingSpotId = d.ParkingSpotId,
                ParkingLabel = d.ParkingSpot == null ? null : d.ParkingSpot.ParkingLabel,
                PropertyId = d.ParkingSpot == null ? null : d.ParkingSpot.PropertyId,
                PropertyName = d.ParkingSpot == null ? null : d.ParkingSpot.Property.PropertyName,
                OwnerId = d.ParkingSpot == null ? null : d.ParkingSpot.OwnerId,
                LastHeartbeatAt = d.LastHeartbeatAt,
                CreatedAt = d.CreatedAt,
                UpdatedAt = d.UpdatedAt
            })
            .ToListAsync(cancellationToken);

    /// <summary>Registers a UUID as an offline, unassigned device without changing an existing record.</summary>
    public async Task<(IoTDeviceResponse Device, bool Created)> RegisterAsync(string deviceUuid, CancellationToken cancellationToken)
    {
        var normalizedUuid = NormalizeUuid(deviceUuid);
        if (normalizedUuid is null)
        {
            throw new IoTDeviceOperationException(StatusCodes.Status400BadRequest, "DeviceUuid must be a valid UUID.");
        }

        var existing = await _db.IoTDevices.SingleOrDefaultAsync(d => d.Esp32Serial == normalizedUuid, cancellationToken);
        if (existing is not null)
        {
            // A successful boot registration proves the device is currently reachable.
            existing.DeviceStatus = DeviceStatus.Online;
            existing.UpdatedAt = DateTime.UtcNow;
            IoTDeviceLogWriter.Add(_db, existing, "device_registration", "already_registered", existing.UpdatedAt,
                details: "Boot registration confirmed the existing device is online.");
            await _db.SaveChangesAsync(cancellationToken);
            return (ToResponse(existing), false);
        }

        var now = DateTime.UtcNow;
        var device = new IoTDevice
        {
            Esp32Serial = normalizedUuid,
            FirmwareVersion = "unknown",
            DeviceStatus = DeviceStatus.Online,
            IsAssigned = false,
            ParkingSpotId = null,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.IoTDevices.Add(device);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            IoTDeviceLogWriter.Add(_db, device, "device_registration", "registered", now,
                details: "Device UUID registered and marked online.");
            await _db.SaveChangesAsync(cancellationToken);
            return (ToResponse(device), true);
        }
        catch (DbUpdateException)
        {
            // A unique UUID constraint makes simultaneous duplicate registration idempotent.
            _db.Entry(device).State = EntityState.Detached;
            var winner = await _db.IoTDevices.SingleOrDefaultAsync(d => d.Esp32Serial == normalizedUuid, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            winner.DeviceStatus = DeviceStatus.Online;
            winner.UpdatedAt = DateTime.UtcNow;
            IoTDeviceLogWriter.Add(_db, winner, "device_registration", "already_registered", winner.UpdatedAt,
                details: "Concurrent boot registration confirmed the existing device is online.");
            await _db.SaveChangesAsync(cancellationToken);
            return (ToResponse(winner), false);
        }
    }

    /// <summary>Creates or replaces the current hashed OTP, enforcing a per-device cooldown.</summary>
    public async Task<IoTOtpResponse> GenerateOtpAsync(string deviceUuid, CancellationToken cancellationToken)
    {
        var normalizedUuid = NormalizeUuid(deviceUuid);
        if (normalizedUuid is null)
        {
            throw new IoTDeviceOperationException(StatusCodes.Status400BadRequest, "DeviceUuid must be a valid UUID.");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var device = await _db.IoTDevices.SingleOrDefaultAsync(d => d.Esp32Serial == normalizedUuid, cancellationToken);
        if (device is null)
        {
            throw new IoTDeviceOperationException(StatusCodes.Status404NotFound, "Device is not registered.");
        }

        var now = DateTime.UtcNow;
        var storedOtp = await _db.IoTDeviceBindingOtps.SingleOrDefaultAsync(o => o.IoTDeviceId == device.IoTDeviceId, cancellationToken);
        if (storedOtp is not null && now - storedOtp.GeneratedAt < OtpCooldown)
        {
            var remainingSeconds = Math.Max(1, (int)Math.Ceiling((OtpCooldown - (now - storedOtp.GeneratedAt)).TotalSeconds));
            IoTDeviceLogWriter.Add(_db, device, "binding_otp_request", "rate_limited", now,
                details: $"RetryAfterSeconds={remainingSeconds}.");
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new IoTDeviceOperationException(StatusCodes.Status429TooManyRequests,
                $"Please wait {remainingSeconds} seconds before requesting another OTP.");
        }

        // Bind resolves by code alone, so avoid collisions with any currently valid code.
        // Keep the existing per-device hashes so codes issued before this change can still be used.
        var activeOtps = await ActiveBindingOtps(now).AsNoTracking().ToListAsync(cancellationToken);
        var otp = GenerateUniqueOtp(activeOtps);
        var otpRecord = storedOtp ?? new IoTDeviceBindingOtp { IoTDeviceId = device.IoTDeviceId };
        otpRecord.OtpHash = HashOtp(normalizedUuid, otp);
        otpRecord.GeneratedAt = now;
        otpRecord.ExpiresAt = now.Add(OtpLifetime);
        if (storedOtp is null)
        {
            _db.IoTDeviceBindingOtps.Add(otpRecord);
        }

        IoTDeviceLogWriter.Add(_db, device, "binding_otp_generated", "success", now,
            details: $"ExpiresAt={otpRecord.ExpiresAt:O}. OTP value excluded.");

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new IoTOtpResponse { Otp = otp, ExpiresAtUtc = otpRecord.ExpiresAt };
    }

    /// <summary>Validates owner, device, OTP, and spot in one transaction before committing the binding.</summary>
    public async Task<IoTDeviceResponse> BindAsync(BindIoTDeviceRequest request, int userId, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var userIsOwner = await _db.Users.AnyAsync(
            u => u.UserId == userId && u.UserType == UserType.PropertyOwner,
            cancellationToken);
        if (!userIsOwner)
        {
            throw new IoTDeviceOperationException(StatusCodes.Status403Forbidden, "Only property owners can bind devices.");
        }

        var parkingSpot = await _db.ParkingSpots.SingleOrDefaultAsync(
            p => p.ParkingSpotId == request.ParkingSpotId && p.OwnerId == userId &&
                p.AvailabilityStatus != AvailabilityStatus.Deleted,
            cancellationToken);
        if (parkingSpot is null)
        {
            throw new IoTDeviceOperationException(StatusCodes.Status404NotFound, "Parking spot was not found for this owner.");
        }

        var now = DateTime.UtcNow;
        var activeOtps = await ActiveBindingOtps(now).ToListAsync(cancellationToken);
        var matches = activeOtps.Where(candidate =>
            MatchesOtp(candidate.IoTDevice.Esp32Serial, request.Otp, candidate.OtpHash)).Take(2).ToList();
        if (matches.Count != 1 || matches[0].ExpiresAt <= DateTime.UtcNow)
        {
            throw new IoTDeviceOperationException(StatusCodes.Status400BadRequest,
                "Pairing code is invalid, expired, or ambiguous. Generate a fresh code on the device and try again.");
        }

        var otpRecord = matches[0];
        var device = otpRecord.IoTDevice;
        if (device.IsAssigned || device.ParkingSpotId.HasValue)
        {
            IoTDeviceLogWriter.Add(_db, device, "device_binding", "rejected_already_assigned", DateTime.UtcNow,
                details: $"userId={userId}");
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new IoTDeviceOperationException(StatusCodes.Status409Conflict, "Device is already assigned.");
        }

        // The unique filtered spot index is the final guard against concurrent double assignment.
        device.ParkingSpotId = parkingSpot.ParkingSpotId;
        device.IsAssigned = true;
        device.UpdatedAt = now;
        _db.IoTDeviceBindingOtps.Remove(otpRecord);
        IoTDeviceLogWriter.Add(_db, device, "device_binding", "success", now,
            details: $"userId={userId}; parkingSpotId={parkingSpot.ParkingSpotId}");

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToResponse(device);
        }
        catch (DbUpdateException ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new IoTDeviceOperationException(StatusCodes.Status409Conflict,
                "The device or parking spot was assigned by another request. Refresh and try again.", ex);
        }
    }

    private IQueryable<IoTDeviceBindingOtp> ActiveBindingOtps(DateTime now)
        => _db.IoTDeviceBindingOtps.Where(record => record.ExpiresAt > now).Include(record => record.IoTDevice);

    private string GenerateUniqueOtp(List<IoTDeviceBindingOtp> activeOtps)
    {
        for (var attempt = 0; attempt < 32; attempt++)
        {
            var otp = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
            if (!activeOtps.Any(record => MatchesOtp(record.IoTDevice.Esp32Serial, otp, record.OtpHash)))
            {
                return otp;
            }
        }

        throw new IoTDeviceOperationException(StatusCodes.Status503ServiceUnavailable,
            "Could not issue a pairing code. Please try again shortly.");
    }

    private string HashOtp(string deviceUuid, string otp)
    {
        var bytes = Encoding.UTF8.GetBytes($"{deviceUuid}:{otp}");
        return Convert.ToHexString(HMACSHA256.HashData(_otpPepper, bytes));
    }

    private bool MatchesOtp(string deviceUuid, string otp, string storedHash)
    {
        try
        {
            var candidate = Convert.FromHexString(HashOtp(deviceUuid, otp));
            var expected = Convert.FromHexString(storedHash);
            return candidate.Length == expected.Length && CryptographicOperations.FixedTimeEquals(candidate, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string? NormalizeUuid(string? value)
        => Guid.TryParseExact(value, "D", out var uuid) ? uuid.ToString("D") : null;

    private static IoTDeviceResponse ToResponse(IoTDevice device) => new()
    {
        DeviceUuid = device.Esp32Serial,
        DeviceStatus = device.DeviceStatus.ToString(),
        IsAssigned = device.IsAssigned,
        ParkingSpotId = device.ParkingSpotId
    };
}

/// <summary>Maps expected IoT validation failures to HTTP status codes at the controller boundary.</summary>
public sealed class IoTDeviceOperationException : Exception
{
    public int StatusCode { get; }

    public IoTDeviceOperationException(int statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
