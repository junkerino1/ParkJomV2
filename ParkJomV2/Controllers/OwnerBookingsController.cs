using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services;
using System.Globalization;
using System.Security.Claims;

namespace ParkJomV2.Controllers;

[ApiController]
[Authorize]
[Route("api/owner/bookings")]
public class OwnerBookingsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly AccessLogService _accessLogService;
    private readonly ILogger<OwnerBookingsController> _logger;

    public OwnerBookingsController(
        ApplicationDbContext context,
        AccessLogService accessLogService,
        ILogger<OwnerBookingsController> logger)
    {
        _context = context;
        _accessLogService = accessLogService;
        _logger = logger;
    }

    /// <summary>
    /// Returns booking summaries only for parking spots owned by the authenticated user,
    /// with optional parking-spot, overlapping calendar-month, and booking-status filters.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(OwnerBookingListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<OwnerBookingListResponse>> GetOwnerBookings(
        [FromQuery] int? spotId = null,
        [FromQuery] string? month = null,
        [FromQuery] string? status = null)
    {
        if (spotId.HasValue && spotId.Value <= 0)
        {
            return BadRequest(new ErrorResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Success = false,
                Message = "spotId must be greater than zero."
            });
        }

        DateOnly? monthStart = null;
        DateOnly? monthEndExclusive = null;
        if (!string.IsNullOrWhiteSpace(month))
        {
            if (!CalendarMonthParser.TryParse(month, out var parsedMonthStart, out var parsedMonthEndExclusive))
            {
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "month must use YYYY-MM format."
                });
            }

            monthStart = parsedMonthStart;
            monthEndExclusive = parsedMonthEndExclusive;
        }

        if (!TryParseBookingStatus(status, out var bookingStatus))
        {
            return BadRequest(new ErrorResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Success = false,
                Message = "status must be one of: Pending, Confirmed, Cancelled, Completed, Expired, Active."
            });
        }

        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        try
        {
            if (spotId.HasValue)
            {
                var requestedSpotOwnerId = await _context.ParkingSpots
                    .AsNoTracking()
                    .Where(spot => spot.ParkingSpotId == spotId.Value)
                    .Select(spot => (int?)spot.OwnerId)
                    .FirstOrDefaultAsync();

                if (!requestedSpotOwnerId.HasValue)
                {
                    return NotFound(new ErrorResponse
                    {
                        Code = StatusCodes.Status404NotFound,
                        Success = false,
                        Message = "Parking spot not found."
                    });
                }

                if (requestedSpotOwnerId.Value != userId)
                {
                    return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                    {
                        Code = StatusCodes.Status403Forbidden,
                        Success = false,
                        Message = "You are not authorized to view bookings for this parking spot."
                    });
                }
            }

            var bookingsQuery = _context.Bookings
                .AsNoTracking()
                .Where(booking => booking.ParkingSpot.OwnerId == userId);

            if (spotId.HasValue)
            {
                bookingsQuery = bookingsQuery.Where(booking => booking.ParkingSpotId == spotId.Value);
            }

            if (monthStart.HasValue && monthEndExclusive.HasValue)
            {
                var monthStartDateTime = monthStart.Value.ToDateTime(TimeOnly.MinValue);
                var monthEndExclusiveDateTime = monthEndExclusive.Value.ToDateTime(TimeOnly.MinValue);
                bookingsQuery = bookingsQuery.Where(booking =>
                    booking.StartDate < monthEndExclusiveDateTime &&
                    booking.EndDate > monthStartDateTime);
            }

            if (bookingStatus.HasValue)
            {
                bookingsQuery = bookingsQuery.Where(booking => booking.BookingStatus == bookingStatus.Value);
            }

            var bookings = await bookingsQuery
                .Include(booking => booking.ParkingSpot)
                .Include(booking => booking.Renter)
                .Include(booking => booking.Vehicle)
                .OrderByDescending(booking => booking.StartDate)
                .ThenByDescending(booking => booking.CreatedAt)
                .ToListAsync();

            var data = bookings
                .Select(MapOwnerBookingSummary)
                .ToList();

            await _accessLogService.LogAsync(
                User,
                "GetOwnerBookings",
                true,
                $"SpotId={spotId?.ToString() ?? "all"}; Month={monthStart?.ToString("yyyy-MM", CultureInfo.InvariantCulture) ?? "all"}; Status={bookingStatus?.ToString() ?? "all"}; Count={data.Count}");

            return Ok(new OwnerBookingListResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = data.Count > 0
                    ? $"Retrieved {data.Count} owner booking(s) successfully."
                    : "No owner bookings found for the selected filters.",
                ParkingSpotId = spotId,
                Month = monthStart?.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                Status = bookingStatus?.ToString(),
                Data = data
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error retrieving owner bookings for user {UserId}",
                userId);
            await _accessLogService.LogAsync(User, "GetOwnerBookings", false, ex.Message);

            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while retrieving owner bookings."
            });
        }
    }

    /// <summary>
    /// Parses an optional case-insensitive booking-status filter without accepting numeric enum values.
    /// </summary>
    private static bool TryParseBookingStatus(string? status, out BookingStatus? bookingStatus)
    {
        bookingStatus = null;
        if (string.IsNullOrWhiteSpace(status))
        {
            return true;
        }

        var matchingStatusName = Enum.GetNames<BookingStatus>()
            .FirstOrDefault(candidate => string.Equals(
                candidate,
                status.Trim(),
                StringComparison.OrdinalIgnoreCase));

        if (matchingStatusName == null)
        {
            return false;
        }

        bookingStatus = Enum.Parse<BookingStatus>(matchingStatusName);
        return true;
    }

    /// <summary>
    /// Maps an owner-authorized booking to a compact summary using inclusive customer-facing dates.
    /// </summary>
    private static OwnerBookingSummaryResponse MapOwnerBookingSummary(Booking booking)
    {
        var startDate = DateOnly.FromDateTime(booking.StartDate);
        var endDate = DateOnly.FromDateTime(booking.EndDate);
        if (booking.EndDate.TimeOfDay == TimeSpan.Zero && endDate > startDate)
        {
            endDate = endDate.AddDays(-1);
        }

        var bookedDays = booking.BookedDays > 0
            ? booking.BookedDays
            : Math.Max(1, endDate.DayNumber - startDate.DayNumber + 1);

        return new OwnerBookingSummaryResponse
        {
            BookingId = booking.BookingId,
            BookingReference = booking.BookingReference,
            ParkingSpotId = booking.ParkingSpotId,
            ParkingLabel = booking.ParkingSpot.ParkingLabel,
            RenterId = booking.RenterId,
            RenterName = $"{booking.Renter.FirstName} {booking.Renter.LastName}".Trim(),
            VehicleId = booking.VehicleId,
            VehicleNumberPlate = booking.Vehicle.NumberPlate,
            StartDate = startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            EndDate = endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            BookedDays = bookedDays,
            BookingStatus = booking.BookingStatus.ToString(),
            RenterTotal = booking.TotalAmount,
            OwnerPayoutAmount = booking.OwnerPayoutAmount,
            CreatedAt = booking.CreatedAt
        };
    }
}
