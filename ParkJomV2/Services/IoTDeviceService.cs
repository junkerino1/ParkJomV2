using System.Data;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.Services;

/// <summary>Manages device inventory, target authorization, combined live scans, and atomic OTP binding.</summary>
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

    public async Task<AdminIoTSupportPage> SearchForSupportAsync(
        string by, string term, int page, int pageSize, CancellationToken cancellationToken)
    {
        by = by.Trim().ToLowerInvariant();
        term = term.Trim();
        if (term.Length == 0 || (by is "ownername" or "rentername" or "propertyname" && term.Length < 2))
            throw InvalidSupportSearch("Enter at least two characters for a name, or a valid ID or reference.");

        var id = int.TryParse(term, out var parsedId) && parsedId > 0 ? parsedId : 0;
        if (by is "ownerid" or "renterid" or "bookingid" or "deviceid" or "parkingspotid" or "propertyid" && id == 0)
            throw InvalidSupportSearch("Enter a positive numeric ID.");

        var bookings = _db.Bookings.AsNoTracking();
        var devices = _db.IoTDevices.AsNoTracking();
        switch (by)
        {
            case "bookingid":
                bookings = bookings.Where(b => b.BookingId == id);
                break;
            case "bookingreference":
                bookings = bookings.Where(b => b.BookingReference == term);
                break;
            case "renterid":
                bookings = bookings.Where(b => b.RenterId == id);
                break;
            case "rentername":
                bookings = bookings.Where(b =>
                    ((b.Renter.FirstName ?? "") + " " + (b.Renter.LastName ?? "")).Contains(term));
                break;
            case "ownername":
                devices = devices.Where(d => d.ParkingSpot != null &&
                    ((d.ParkingSpot.Owner.FirstName ?? "") + " " + (d.ParkingSpot.Owner.LastName ?? "")).Contains(term));
                break;
            case "ownerid":
                devices = devices.Where(d => d.ParkingSpot != null && d.ParkingSpot.OwnerId == id);
                break;
            case "deviceid":
                devices = devices.Where(d => d.IoTDeviceId == id);
                break;
            case "deviceuuid":
                var uuid = NormalizeUuid(term) ?? throw InvalidSupportSearch("Enter a valid device UUID.");
                devices = devices.Where(d => d.Esp32Serial == uuid);
                break;
            case "parkingspotid":
                devices = devices.Where(d => d.ParkingSpotId == id);
                break;
            case "propertyid":
                devices = devices.Where(d => d.ParkingSpot != null && d.ParkingSpot.PropertyId == id);
                break;
            case "propertyname":
                devices = devices.Where(d => d.ParkingSpot != null && d.ParkingSpot.Property.PropertyName.Contains(term));
                break;
            default:
                throw InvalidSupportSearch("The search type is not supported.");
        }

        if (by is "bookingid" or "bookingreference" or "renterid" or "rentername")
            devices = devices.Where(d => bookings.Any(b => b.ParkingSpotId == d.ParkingSpotId));

        var items = await devices.OrderBy(d => d.IoTDeviceId)
            .Skip((page - 1) * pageSize).Take(pageSize + 1)
            .Select(d => new AdminIoTSupportDevice(
                d.IoTDeviceId,
                d.Esp32Serial,
                d.DeviceStatus.ToString(),
                d.FirmwareVersion,
                d.LastHeartbeatAt,
                d.IsAssigned,
                d.IsAssigned && d.Esp32Serial != "" &&
                    d.ParkingSpot != null && d.ParkingSpot.AvailabilityStatus != AvailabilityStatus.Deleted,
                d.ParkingSpot == null ? null : new AdminIoTSupportSpot(
                    d.ParkingSpot.ParkingSpotId,
                    d.ParkingSpot.ParkingLabel,
                    d.ParkingSpot.AvailabilityStatus.ToString(),
                    d.ParkingSpot.IsPublished,
                    new AdminIoTSupportPerson(d.ParkingSpot.OwnerId,
                        ((d.ParkingSpot.Owner.FirstName ?? "") + " " + (d.ParkingSpot.Owner.LastName ?? "")).Trim()),
                    new AdminIoTSupportProperty(d.ParkingSpot.PropertyId, d.ParkingSpot.Property.PropertyName,
                        d.ParkingSpot.Property.Address, d.ParkingSpot.Property.Latitude, d.ParkingSpot.Property.Longitude)),
                bookings.Where(b => b.ParkingSpotId == d.ParkingSpotId)
                    .OrderByDescending(b => b.BookingStatus == BookingStatus.Active && b.ActualExitAt == null)
                    .ThenByDescending(b => b.StartDate)
                    .ThenByDescending(b => b.BookingId)
                    .Select(b => new AdminIoTSupportBooking(
                        b.BookingId, b.BookingReference, b.BookingStatus.ToString(), b.StartDate, b.EndDate,
                        b.CheckedInAt, b.ActualExitAt,
                        new AdminIoTSupportPerson(b.RenterId,
                            ((b.Renter.FirstName ?? "") + " " + (b.Renter.LastName ?? "")).Trim())))
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

        var hasMore = items.Count > pageSize;
        if (hasMore) items.RemoveAt(pageSize);
        return new AdminIoTSupportPage(items, page, pageSize, hasMore);
    }

    private static IoTDeviceOperationException InvalidSupportSearch(string message)
        => new(StatusCodes.Status400BadRequest, message);

    /// <summary>Resolves one target and enforces access before any hardware request.</summary>
    public async Task<IoTDevice> ResolveDeviceAsync(
        IoTDeviceTarget target, int userId, UserType userType, CancellationToken cancellationToken,
        bool adminControl = false)
    {
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(target, new ValidationContext(target), errors, true))
            throw new IoTDeviceOperationException(StatusCodes.Status400BadRequest, string.Join(" ", errors.Select(e => e.ErrorMessage)));

        if (adminControl && userType != UserType.Admin)
            throw new IoTDeviceOperationException(StatusCodes.Status403Forbidden, "Only administrators can use admin control.");

        IQueryable<IoTDevice> devices = _db.IoTDevices.AsNoTracking()
            .Include(d => d.ParkingSpot).ThenInclude(p => p!.Property);
        if (target.BookingId is int bookingId)
        {
            if (!adminControl && userType != UserType.Renter)
                throw new IoTDeviceOperationException(StatusCodes.Status403Forbidden, "Booking status requires the booking renter's token.");

            var booking = await _db.Bookings.AsNoTracking()
                .Where(b => b.BookingId == bookingId && (adminControl || b.RenterId == userId))
                .Select(b => new { b.ParkingSpotId, b.BookingStatus })
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new IoTDeviceOperationException(StatusCodes.Status404NotFound, "Booking not found.");
            if (!adminControl && booking.BookingStatus is not (BookingStatus.Confirmed or BookingStatus.Active))
                throw new IoTDeviceOperationException(StatusCodes.Status409Conflict, "Bollard status requires a confirmed or active booking.");
            devices = devices.Where(d => d.ParkingSpotId == booking.ParkingSpotId && d.IsAssigned);
        }
        else
        {
            if (userType is not (UserType.Admin or UserType.PropertyOwner))
                throw new IoTDeviceOperationException(StatusCodes.Status403Forbidden, "Device and parking spot targets require an owner or admin token.");
            if (target.DeviceUuid is string uuid)
            {
                var normalizedUuid = NormalizeUuid(uuid)!;
                devices = devices.Where(d => d.Esp32Serial == normalizedUuid);
            }
            else
                devices = devices.Where(d => d.ParkingSpotId == target.ParkingSpotId && d.IsAssigned);
            if (userType == UserType.PropertyOwner)
                devices = devices.Where(d => d.ParkingSpot != null && d.ParkingSpot.OwnerId == userId);
        }

        var device = await devices.SingleOrDefaultAsync(cancellationToken)
            ?? throw new IoTDeviceOperationException(StatusCodes.Status404NotFound, "Device not found or not available to this account.");
        if (string.IsNullOrWhiteSpace(device.Esp32Serial) ||
            (adminControl && (!device.IsAssigned || device.ParkingSpot == null ||
                device.ParkingSpot.AvailabilityStatus == AvailabilityStatus.Deleted)))
            throw new IoTDeviceOperationException(StatusCodes.Status409Conflict, "No controllable assigned device is available for this target.");
        return device;
    }

    /// <summary>Reads the latest valid device ACK/telemetry log without contacting MQTT.</summary>
    public async Task<IoTStatusResponse> GetStatusAsync(
        IoTDeviceTarget target, int userId, UserType userType, CancellationToken cancellationToken)
    {
        var device = await ResolveDeviceAsync(target, userId, userType, cancellationToken);
        var topic = $"{_mqttTopicRoot}/{device.Esp32Serial}";
        var logs = _db.IoTStatusLogs.AsNoTracking()
            .Where(log => log.IoTDeviceId == device.IoTDeviceId &&
                (log.Topic == topic + "/ack" || log.Topic == topic + "/telemetry"))
            .OrderByDescending(log => log.LoggedAt).ThenByDescending(log => log.IoTStatusLogId)
            .Select(log => new { log.Message, log.LoggedAt });
        await foreach (var log in logs.AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            if (IoTStatusReading.TryParse(log.Message, out var reading))
                return CreateStatusResponse(device, target.BookingId, reading, AsUtc(log.LoggedAt));
        }
        return CreateStatusResponse(device, target.BookingId, null, null);
    }
    /// <summary>Reads distance and position together over MQTT; never moves the bollard.</summary>
    public async Task<IoTStatusResponse> ScanStatusAsync(
        IoTDeviceTarget target, int userId, UserType userType, IMqttService mqtt, CancellationToken cancellationToken)
    {
        var device = await ResolveDeviceAsync(target, userId, userType, cancellationToken);
        MqttCommandResult response;
        try
        {
            response = await mqtt.RequestStatusScanAsync(device.Esp32Serial, cancellationToken);
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

        if (response.ReceivedAt is not DateTimeOffset receivedAt || response.Result is null ||
            !IoTStatusReading.TryCreate(response.DistanceCm, response.BollardPosition, response.Result, out var reading))
            throw new IoTDeviceOperationException(StatusCodes.Status502BadGateway, "The bollard returned an invalid or incomplete status reading.");

        // MQTT persists the log and LastHeartbeatAt before completing the pending scan.
        device.LastHeartbeatAt = await _db.IoTDevices.AsNoTracking().Where(d => d.IoTDeviceId == device.IoTDeviceId)
            .Select(d => d.LastHeartbeatAt).SingleAsync(cancellationToken);
        return CreateStatusResponse(device, target.BookingId, reading, receivedAt);
    }

    private static DateTimeOffset AsUtc(DateTime value)
        => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static IoTStatusResponse CreateStatusResponse(
        IoTDevice device, int? bookingId, IoTStatusReading? reading, DateTimeOffset? receivedAt)
    {
        DateTimeOffset? lastHeartbeatAt = device.LastHeartbeatAt is DateTime lastSeen ? AsUtc(lastSeen) : null;
        // Existing valid logs also prove device contact, even before LastHeartbeatAt was populated.
        if (receivedAt.HasValue && (!lastHeartbeatAt.HasValue || receivedAt.Value > lastHeartbeatAt.Value))
            lastHeartbeatAt = receivedAt;
        var checkedAt = DateTimeOffset.UtcNow;
        var heartbeatStatus = lastHeartbeatAt is not DateTimeOffset heartbeat ? "unknown"
            : checkedAt - heartbeat < TimeSpan.FromSeconds(HeartbeatOfflineAfterSeconds) ? "online" : "offline";
        var spot = device.ParkingSpot;
        return new IoTStatusResponse(device.Esp32Serial, bookingId, heartbeatStatus, lastHeartbeatAt,
            checkedAt, HeartbeatOfflineAfterSeconds, reading?.DistanceCm, reading?.SensorStatus ?? "unknown",
            reading?.BollardPosition, receivedAt,
            spot == null ? null : new IoTStatusParkingSpotResponse(spot.ParkingSpotId, spot.ParkingLabel,
                spot.PropertyId, spot.Property.PropertyName));
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

    /// <summary>Registers a UUID and issues its generated BLE secret once without changing an existing assignment.</summary>
    public async Task<(IoTDeviceResponse Device, bool Created, string? IssuedSecret)> RegisterAsync(
        string deviceUuid,
        CancellationToken cancellationToken)
    {
        var normalizedUuid = NormalizeUuid(deviceUuid);
        if (normalizedUuid is null)
        {
            throw new IoTDeviceOperationException(StatusCodes.Status400BadRequest, "DeviceUuid must be a valid UUID.");
        }

        var existing = await _db.IoTDevices.SingleOrDefaultAsync(d => d.Esp32Serial == normalizedUuid, cancellationToken);
        if (existing is not null)
        {
            // Existing rows from before this feature receive a secret on their next boot registration.
            // Once stored, subsequent anonymous registrations must not disclose it again.
            string? issuedSecret = null;
            if (string.IsNullOrWhiteSpace(existing.DeviceSecret))
            {
                existing.DeviceSecret = GenerateDeviceSecret();
                issuedSecret = existing.DeviceSecret;
            }

            // A successful boot registration proves the device is currently reachable.
            existing.DeviceStatus = DeviceStatus.Online;
            existing.UpdatedAt = DateTime.UtcNow;
            IoTDeviceLogWriter.Add(_db, existing, "device_registration", "already_registered", existing.UpdatedAt,
                details: issuedSecret is null
                    ? "Boot registration confirmed the existing device is online."
                    : "Boot registration confirmed the device and provisioned its database-managed secret.");
            await _db.SaveChangesAsync(cancellationToken);
            return (ToResponse(existing), false, issuedSecret);
        }

        var now = DateTime.UtcNow;
        var device = new IoTDevice
        {
            Esp32Serial = normalizedUuid,
            FirmwareVersion = "unknown",
            DeviceSecret = GenerateDeviceSecret(),
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
            return (ToResponse(device), true, device.DeviceSecret);
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
            return (ToResponse(winner), false, null);
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

    private static string GenerateDeviceSecret()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

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
