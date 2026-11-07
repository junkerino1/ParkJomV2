using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services;
using ParkJomV2.Services.Copilot;
using ParkJomV2.Services.Vision;

namespace ParkJomV2.Controllers;

/// <summary>Property-scoped booking, access-event, and vehicle plate checks for management users.</summary>
[ApiController]
[Authorize(Policy = "PropertyManagementOnly")]
[Route("api/management")]
public sealed class PropertyManagementController : ControllerBase
{
    private const int MaximumPageSize = 100;
    private readonly ApplicationDbContext _db;
    private readonly CurrentUserService _currentUser;
    private readonly VehicleVisionService _vision;
    private readonly AccessLogService _accessLogs;
    private readonly IConfiguration _configuration;

    public PropertyManagementController(
        ApplicationDbContext db,
        CurrentUserService currentUser,
        VehicleVisionService vision,
        AccessLogService accessLogs,
        IConfiguration configuration)
    {
        _db = db;
        _currentUser = currentUser;
        _vision = vision;
        _accessLogs = accessLogs;
        _configuration = configuration;
    }

    /// <summary>Lists bookings belonging to the property assigned to the authenticated manager.</summary>
    /// <remarks>Supports optional parkingSpotId, status, fromDate, toDate, page, and pageSize filters.</remarks>
    /// <response code="200">Property bookings returned.</response>
    /// <response code="401">Authentication is required.</response>
    /// <response code="403">The account is not authorized or must be activated first.</response>
    [HttpGet("booking")]
    [ProducesResponseType(typeof(PropertyManagementBookingListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PropertyManagementBookingListResponse>> GetBookings(
        [FromQuery] int? parkingSpotId,
        [FromQuery] string? status,
        [FromQuery] DateTime? fromDate,
        [FromQuery] DateTime? toDate,
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, MaximumPageSize)] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var account = await GetAuthorizedAccountAsync(cancellationToken);
        if (account is null)
        {
            await AuditAsync("ListBookings", false, null, "Account unavailable or activation required.");
            return Forbid();
        }
        if (parkingSpotId is < 1 || fromDate > toDate)
        {
            await AuditAsync("ListBookings", false, account, "Invalid filters.");
            return BadRequest(Error(StatusCodes.Status400BadRequest, "INVALID_FILTER", "The booking filters are invalid."));
        }
        if (!string.IsNullOrWhiteSpace(status) && !Enum.TryParse<BookingStatus>(status, true, out _))
        {
            await AuditAsync("ListBookings", false, account, $"Invalid status filter={status}.");
            return BadRequest(Error(StatusCodes.Status400BadRequest, "INVALID_STATUS", "status is not a recognized booking status."));
        }

        var query = _db.Bookings.AsNoTracking().Where(b => b.ParkingSpot.PropertyId == account.PropertyId);
        if (parkingSpotId.HasValue) query = query.Where(b => b.ParkingSpotId == parkingSpotId.Value);
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<BookingStatus>(status, true, out var parsedStatus))
            query = query.Where(b => b.BookingStatus == parsedStatus);
        if (fromDate.HasValue) query = query.Where(b => b.EndDate >= fromDate.Value);
        if (toDate.HasValue) query = query.Where(b => b.StartDate <= toDate.Value);

        var total = await query.CountAsync(cancellationToken);
        var rows = await query.Include(b => b.ParkingSpot).Include(b => b.Vehicle)
            .OrderByDescending(b => b.StartDate).Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(cancellationToken);
        await AuditAsync("ListBookings", true, account, $"count={rows.Count}; page={page}; pageSize={pageSize}; spotId={parkingSpotId}; status={status}.");
        return Ok(new PropertyManagementBookingListResponse
        {
            Code = StatusCodes.Status200OK, Success = true, Message = "Property bookings retrieved.",
            TotalCount = total, Page = page, PageSize = pageSize,
            Data = rows.Select(MapBooking).ToList()
        });
    }

    /// <summary>Returns a booking only when it belongs to the manager's assigned property.</summary>
    /// <response code="200">Booking details returned.</response>
    /// <response code="403">The booking belongs to another property or the account is not activated.</response>
    /// <response code="404">Booking not found.</response>
    [HttpGet("booking/{id:int}")]
    [ProducesResponseType(typeof(PropertyManagementBookingDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PropertyManagementBookingDTO>> GetBooking(int id, CancellationToken cancellationToken)
    {
        var account = await GetAuthorizedAccountAsync(cancellationToken);
        if (account is null)
        {
            await AuditAsync("ViewBooking", false, null, $"bookingId={id}; account unavailable or activation required.");
            return Forbid();
        }
        var booking = await _db.Bookings.AsNoTracking()
            .Include(b => b.ParkingSpot).Include(b => b.Vehicle)
            .FirstOrDefaultAsync(b => b.BookingId == id && b.ParkingSpot.PropertyId == account.PropertyId, cancellationToken);
        if (booking is null)
        {
            await AuditAsync("ViewBooking", false, account, $"bookingId={id}; not found in assigned property.");
            return NotFound(Error(StatusCodes.Status404NotFound, "BOOKING_NOT_FOUND", "Booking was not found."));
        }
        await AuditAsync("ViewBooking", true, account, $"bookingId={id}.", id);
        return Ok(MapBooking(booking));
    }

    /// <summary>Lists physical entry events recorded by IoT devices at the manager's property.</summary>
    /// <remarks>Events use the existing IoTStatusLogs storage and therefore follow its configured retention period.</remarks>
    /// <response code="200">Property entry events returned.</response>
    /// <response code="403">The account is not authorized or must be activated first.</response>
    [HttpGet("access-events")]
    [ProducesResponseType(typeof(PropertyManagementAccessEventListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PropertyManagementAccessEventListResponse>> GetAccessEvents(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int? parkingSpotId,
        [FromQuery] string? plate,
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, MaximumPageSize)] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var account = await GetAuthorizedAccountAsync(cancellationToken);
        if (account is null)
        {
            await AuditAsync("ListAccessEvents", false, null, "Account unavailable or activation required.");
            return Forbid();
        }
        if (from > to || parkingSpotId is < 1)
        {
            await AuditAsync("ListAccessEvents", false, account, "Invalid filters.");
            return BadRequest(Error(StatusCodes.Status400BadRequest, "INVALID_FILTER", "The access-event filters are invalid."));
        }

        var query = _db.IoTStatusLogs.AsNoTracking()
            .Where(log => log.Topic == "backend/parking_entry_confirmed" &&
                          log.IoTDevice.ParkingSpot != null &&
                          log.IoTDevice.ParkingSpot.PropertyId == account.PropertyId);
        if (from.HasValue) query = query.Where(log => log.LoggedAt >= from.Value.UtcDateTime);
        if (to.HasValue) query = query.Where(log => log.LoggedAt <= to.Value.UtcDateTime);
        if (parkingSpotId.HasValue) query = query.Where(log => log.IoTDevice.ParkingSpotId == parkingSpotId.Value);
        var rows = await query.Include(log => log.IoTDevice).ThenInclude(device => device.ParkingSpot)
            .OrderByDescending(log => log.LoggedAt).ToListAsync(cancellationToken);
        var parsed = rows.Select(ParseEntryEvent)
            .Where(item => item is not null)
            .Select(item => item!)
            .Where(item => string.IsNullOrWhiteSpace(plate) || item.VehicleNumberPlate.Contains(plate.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();
        var total = parsed.Count;
        await AuditAsync("ListAccessEvents", true, account, $"returned={Math.Min(total, pageSize)}; page={page}; spotId={parkingSpotId}; plateFilterProvided={!string.IsNullOrWhiteSpace(plate)}.");
        return Ok(new PropertyManagementAccessEventListResponse
        {
            Code = StatusCodes.Status200OK, Success = true, Message = "Property access events retrieved.",
            TotalCount = total, Page = page, PageSize = pageSize,
            Data = parsed.Skip((page - 1) * pageSize).Take(pageSize).ToList()
        });
    }

    /// <summary>Lists recent plate-check attempts made by the current manager.</summary>
    /// <remarks>Plate checks are recorded in the existing AccessLogs table; no new log entity is introduced.</remarks>
    /// <response code="200">Plate-check history returned.</response>
    /// <response code="403">The account is not authorized or must be activated first.</response>
    [HttpGet("plate-check")]
    [ProducesResponseType(typeof(PropertyManagementPlateCheckLogListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PropertyManagementPlateCheckLogListResponse>> GetPlateCheckHistory(
        [FromQuery, Range(1, int.MaxValue)] int page = 1,
        [FromQuery, Range(1, MaximumPageSize)] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var account = await GetAuthorizedAccountAsync(cancellationToken);
        if (account is null)
        {
            await AuditAsync("ListPlateCheckHistory", false, null, "Account unavailable or activation required.");
            return Forbid();
        }
        var query = _db.AccessLogs.AsNoTracking()
            .Where(log => log.Actions.StartsWith("PropertyManagement:PlateCheck") &&
                          log.Actions.Contains($"managementAccountId={account.ManagementAccountId};"));
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(log => log.AccessedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        await AuditAsync("ListPlateCheckHistory", true, account, $"returned={rows.Count}; page={page};");
        return Ok(new PropertyManagementPlateCheckLogListResponse
        {
            Code = StatusCodes.Status200OK, Success = true, Message = "Plate-check history retrieved.",
            TotalCount = total, Page = page, PageSize = pageSize,
            Data = rows.Select(log => new PropertyManagementPlateCheckLogDTO
            {
                AccessLogId = log.AccessLogId,
                CheckedAtUtc = log.AccessedAt,
                Outcome = log.Actions.Contains("[Success]", StringComparison.Ordinal) ? "allowed" : "denied",
                Details = log.Actions,
                BookingId = log.BookingId
            }).ToList()
        });
    }

    /// <summary>Scans a vehicle plate and checks for a matching booking currently eligible at this property.</summary>
    /// <remarks>The API derives the plate from the image and compares it to current bookings; callers cannot submit an expected plate or mark a vehicle as entered.</remarks>
    /// <response code="200">The scan completed; data.allowed indicates whether access is allowed.</response>
    /// <response code="403">The account is not authorized or must be activated first.</response>
    /// <response code="422">A readable plate could not be detected.</response>
    [HttpPost("plate-check")]
    [ProducesResponseType(typeof(PropertyManagementPlateCheckResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PropertyManagementPlateCheckResponse>> CheckPlate(
        [FromBody] PropertyManagementPlateCheckRequest request,
        CancellationToken cancellationToken)
    {
        var account = await GetAuthorizedAccountAsync(cancellationToken);
        if (account is null)
        {
            await AuditAsync("PlateCheck", false, null, "Account unavailable or activation required.");
            return Forbid();
        }
        if (request.ParkingSpotId is < 1)
        {
            await AuditAsync("PlateCheck", false, account, "Invalid parkingSpotId.");
            return BadRequest(Error(StatusCodes.Status400BadRequest, "INVALID_PARKING_SPOT", "parkingSpotId must be a positive value."));
        }
        if (request.ParkingSpotId.HasValue && !await _db.ParkingSpots.AnyAsync(
                spot => spot.ParkingSpotId == request.ParkingSpotId.Value && spot.PropertyId == account.PropertyId, cancellationToken))
        {
            await AuditAsync("PlateCheck", false, account, $"spotId={request.ParkingSpotId}; spot is outside assigned property.");
            return NotFound(Error(StatusCodes.Status404NotFound, "PARKING_SPOT_NOT_FOUND", "The parking spot is not part of your property."));
        }

        VehicleVisionScanDataDto scan;
        try
        {
            scan = await _vision.ScanVehicleAsync(new VehicleVisionScanRequest
            {
                ImageBase64 = request.ImageBase64,
                MimeType = request.MimeType
            }, cancellationToken);
        }
        catch (GeminiException ex)
        {
            await AuditAsync("PlateCheck", false, account, $"Scan failed; status={ex.StatusCode}.");
            return StatusCode(ex.StatusCode, Error(ex.StatusCode, "PLATE_SCAN_FAILED", ex.Message));
        }

        var minimumConfidence = _configuration.GetValue<double?>("PropertyManagement:PlateRecognitionMinimumConfidence") ?? 0.80;
        var now = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8));
        var localDate = DateOnly.FromDateTime(now.DateTime);
        var localTime = TimeOnly.FromDateTime(now.DateTime);
        var normalizedPlate = MalaysianPlateValidator.NormalizeAndValidate(scan.NumberPlate);
        var candidates = normalizedPlate.IsValid && scan.Confidence >= minimumConfidence
            ? await _db.Bookings.AsNoTracking()
                .Include(item => item.Vehicle)
                .Include(item => item.ParkingSpot).ThenInclude(spot => spot.ParkingAvailabilities)
                .Where(item => item.ParkingSpot.PropertyId == account.PropertyId &&
                               (request.ParkingSpotId == null || item.ParkingSpotId == request.ParkingSpotId) &&
                               (item.BookingStatus == BookingStatus.Confirmed || item.BookingStatus == BookingStatus.Active) &&
                               item.StartDate <= now.DateTime && item.EndDate > now.DateTime)
                .OrderByDescending(item => item.BookingStatus == BookingStatus.Active)
                .ToListAsync(cancellationToken)
            : new List<Booking>();
        var booking = candidates.FirstOrDefault(item =>
        {
            var storedPlate = MalaysianPlateValidator.NormalizeAndValidate(item.Vehicle.NumberPlate);
            return storedPlate.IsValid && storedPlate.CompactPlate == normalizedPlate.CompactPlate &&
                   IsWithinAvailabilityWindow(item.ParkingSpot.ParkingAvailabilities, localDate, localTime);
        });

        var hasPlateBooking = candidates.Any(item =>
        {
            var storedPlate = MalaysianPlateValidator.NormalizeAndValidate(item.Vehicle.NumberPlate);
            return storedPlate.IsValid && storedPlate.CompactPlate == normalizedPlate.CompactPlate;
        });
        var inAvailability = booking is not null;
        var allowed = booking is not null && inAvailability;
        var decision = !normalizedPlate.IsValid ? "INVALID_PLATE"
            : scan.Confidence < minimumConfidence ? "PLATE_CONFIDENCE_TOO_LOW"
            : booking is null && hasPlateBooking ? "BOOKING_ACCESS_WINDOW_CLOSED"
            : booking is null ? "NO_ELIGIBLE_BOOKING"
            : "BOOKING_MATCH";
        var message = allowed ? "Vehicle plate matches an eligible booking." : decision switch
        {
            "INVALID_PLATE" => "The detected plate is not in a recognized Malaysian format.",
            "PLATE_CONFIDENCE_TOO_LOW" => "The plate scan confidence is below the configured threshold.",
            "BOOKING_ACCESS_WINDOW_CLOSED" => "The matching booking is outside its allowed parking day or time.",
            _ => "No eligible booking for this vehicle was found at this property."
        };

        var iotDeviceId = booking is null
            ? null
            : await _db.IoTDevices.Where(device => device.ParkingSpotId == booking.ParkingSpotId)
                .Select(device => (int?)device.IoTDeviceId).FirstOrDefaultAsync(cancellationToken);
        await AuditAsync("PlateCheck", allowed, account,
            $"plate={normalizedPlate.CompactPlate}; decision={decision}; confidence={scan.Confidence.ToString("F3", CultureInfo.InvariantCulture)}; spotId={booking?.ParkingSpotId ?? request.ParkingSpotId};",
            booking?.BookingId, iotDeviceId);

        return Ok(new PropertyManagementPlateCheckResponse
        {
            Code = StatusCodes.Status200OK, Success = true, Message = message,
            Data = new PropertyManagementPlateCheckData
            {
                Allowed = allowed, DecisionCode = decision, PropertyId = account.PropertyId,
                BookingId = booking?.BookingId, ParkingSpotId = booking?.ParkingSpotId ?? request.ParkingSpotId,
                DetectedPlate = scan.NumberPlate, FormattedPlate = scan.FormattedPlate,
                Confidence = scan.Confidence, MinimumConfidence = minimumConfidence
            }
        });
    }

    private async Task<PropertyManagement?> GetAuthorizedAccountAsync(CancellationToken cancellationToken)
    {
        if (!_currentUser.UserId.HasValue) return null;
        var account = await _db.PropertyManagements.Include(item => item.Property)
            .FirstOrDefaultAsync(item => item.ManagementAccountId == _currentUser.UserId.Value, cancellationToken);
        return account is not null && account.AccountStatus == "Active" && !account.MustChangePassword ? account : null;
    }

    private Task AuditAsync(string action, bool succeeded, PropertyManagement? account, string detail, int? bookingId = null, int? iotDeviceId = null)
    {
        HttpContext.Items["PropertyManagementAuditRecorded"] = true;
        return _accessLogs.LogManagementAsync(
            account?.ManagementAccountId ?? _currentUser.UserId,
            account?.PropertyId,
            action,
            succeeded,
            detail,
            bookingId,
            iotDeviceId);
    }

    private static PropertyManagementBookingDTO MapBooking(Booking booking) => new()
    {
        BookingId = booking.BookingId,
        BookingReference = booking.BookingReference,
        ParkingSpotId = booking.ParkingSpotId,
        ParkingLabel = booking.ParkingSpot.ParkingLabel,
        VehicleNumberPlate = booking.Vehicle.NumberPlate,
        BookingStatus = booking.BookingStatus.ToString(),
        StartDate = booking.StartDate,
        EndDate = booking.EndDate,
        CheckedInAt = booking.CheckedInAt,
        ActualExitAt = booking.ActualExitAt
    };

    private static PropertyManagementAccessEventDTO? ParseEntryEvent(IoTStatusLog log)
    {
        if (string.IsNullOrWhiteSpace(log.Message)) return null;
        try
        {
            using var document = JsonDocument.Parse(log.Message);
            var root = document.RootElement;
            var details = root.TryGetProperty("details", out var detailsNode) ? detailsNode.GetString() ?? string.Empty : string.Empty;
            int.TryParse(ReadField(details, "bookingId"), out var bookingId);
            var plate = ReadField(details, "vehiclePlate") ?? string.Empty;
            var firstEntry = bool.TryParse(ReadField(details, "firstEntry"), out var parsedFirst) && parsedFirst;
            var eventTime = DateTime.TryParse(ReadField(details, "eventAtUtc"), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedTime) ? parsedTime : log.LoggedAt;
            return new PropertyManagementAccessEventDTO
            {
                IoTStatusLogId = log.IoTStatusLogId,
                BookingId = bookingId,
                ParkingSpotId = log.IoTDevice.ParkingSpotId ?? 0,
                ParkingLabel = log.IoTDevice.ParkingSpot?.ParkingLabel,
                VehicleNumberPlate = plate,
                EnteredAtUtc = eventTime,
                FirstEntryForBooking = firstEntry
            };
        }
        catch (JsonException) { return null; }
    }

    private static string? ReadField(string details, string key)
    {
        foreach (var part in details.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length == 2 && string.Equals(pair[0], key, StringComparison.OrdinalIgnoreCase)) return pair[1];
        }
        return null;
    }

    private static bool IsWithinAvailabilityWindow(IEnumerable<Availability> rules, DateOnly date, TimeOnly time)
        => rules.Any(rule =>
            (!rule.EffectiveFrom.HasValue || date >= rule.EffectiveFrom.Value) &&
            (!rule.EffectiveUntil.HasValue || date <= rule.EffectiveUntil.Value) &&
            (rule.DayType == DayType.Everyday ||
             (rule.DayType == DayType.Weekday && date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) ||
             (rule.DayType == DayType.Weekend && date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)) &&
            time >= rule.StartTime &&
            (time < rule.EndTime || (rule.StartTime == TimeOnly.MinValue && rule.EndTime == new TimeOnly(23, 59) && time.Hour == 23 && time.Minute == 59)));

    private static ErrorResponse Error(int code, string errorCode, string message)
        => new() { Code = code, Success = false, Message = message };
}
