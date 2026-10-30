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

    private readonly ApplicationDbContext _db;
    private readonly byte[] _otpPepper;

    public IoTDeviceService(ApplicationDbContext db, IConfiguration configuration)
    {
        _db = db;
        var pepper = configuration["IoT:OtpPepper"] ?? configuration["JwtSettings:SecretKey"];
        if (string.IsNullOrWhiteSpace(pepper))
        {
            throw new InvalidOperationException("Configure IoT:OtpPepper before using device binding OTPs.");
        }

        _otpPepper = Encoding.UTF8.GetBytes(pepper);
    }

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
            throw new IoTDeviceOperationException(StatusCodes.Status429TooManyRequests,
                $"Please wait {remainingSeconds} seconds before requesting another OTP.");
        }

        // A new code atomically replaces the old one, so only one code can be valid per device.
        var otp = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var otpRecord = storedOtp ?? new IoTDeviceBindingOtp { IoTDeviceId = device.IoTDeviceId };
        otpRecord.OtpHash = HashOtp(normalizedUuid, otp);
        otpRecord.GeneratedAt = now;
        otpRecord.ExpiresAt = now.Add(OtpLifetime);
        if (storedOtp is null)
        {
            _db.IoTDeviceBindingOtps.Add(otpRecord);
        }

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new IoTOtpResponse { Otp = otp, ExpiresAtUtc = otpRecord.ExpiresAt };
    }

    /// <summary>Validates owner, device, OTP, and spot in one transaction before committing the binding.</summary>
    public async Task<IoTDeviceResponse> BindAsync(BindIoTDeviceRequest request, int userId, CancellationToken cancellationToken)
    {
        var normalizedUuid = NormalizeUuid(request.DeviceUuid);
        if (normalizedUuid is null)
        {
            throw new IoTDeviceOperationException(StatusCodes.Status400BadRequest, "DeviceUuid must be a valid UUID.");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var userIsOwner = await _db.Users.AnyAsync(
            u => u.UserId == userId && u.UserType == UserType.PropertyOwner,
            cancellationToken);
        if (!userIsOwner)
        {
            throw new IoTDeviceOperationException(StatusCodes.Status403Forbidden, "Only property owners can bind devices.");
        }

        var device = await _db.IoTDevices
            .Include(d => d.BindingOtp)
            .SingleOrDefaultAsync(d => d.Esp32Serial == normalizedUuid, cancellationToken);
        if (device is null)
        {
            throw new IoTDeviceOperationException(StatusCodes.Status404NotFound, "Device is not registered.");
        }

        if (device.IsAssigned || device.ParkingSpotId.HasValue)
        {
            throw new IoTDeviceOperationException(StatusCodes.Status409Conflict, "Device is already assigned.");
        }

        var parkingSpot = await _db.ParkingSpots.SingleOrDefaultAsync(
            p => p.ParkingSpotId == request.ParkingSpotId && p.OwnerId == userId,
            cancellationToken);
        if (parkingSpot is null)
        {
            throw new IoTDeviceOperationException(StatusCodes.Status404NotFound, "Parking spot was not found for this owner.");
        }

        var otpRecord = device.BindingOtp;
        var now = DateTime.UtcNow;
        if (otpRecord is null || otpRecord.ExpiresAt <= now || !MatchesOtp(normalizedUuid, request.Otp, otpRecord.OtpHash))
        {
            throw new IoTDeviceOperationException(StatusCodes.Status400BadRequest, "OTP is invalid or expired.");
        }

        // The unique filtered spot index is the final guard against concurrent double assignment.
        device.ParkingSpotId = parkingSpot.ParkingSpotId;
        device.IsAssigned = true;
        device.UpdatedAt = now;
        _db.IoTDeviceBindingOtps.Remove(otpRecord);

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
