using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services;
using System.Globalization;

namespace ParkJomV2.Controllers;

[ApiController]
[Authorize]
[Route("api/owner/bookings")]
public class OwnerBookingsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly CurrentUserService _currentUser;
    private readonly AccessLogService _accessLogService;
    private readonly ILogger<OwnerBookingsController> _logger;

    public OwnerBookingsController(
        ApplicationDbContext context,
        CurrentUserService currentUser,
        AccessLogService accessLogService,
        ILogger<OwnerBookingsController> logger)
    {
        _context = context;
        _currentUser = currentUser;
        _accessLogService = accessLogService;
        _logger = logger;
    }

    /// <summary>
    /// Returns booking summaries only for parking spots owned by the authenticated user,
    /// filter: spotId (optional), month (YYYY-MM, optional), status (Pending, Confirmed, Cancelled, Completed, Expired, Active; optional).
    /// </summary>
    [HttpGet]
    [HttpGet("/api/parking/bookings/history")]
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

        var user = await _currentUser.GetCurrentUserAsync();

        if(user == null)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
            {
                Code = StatusCodes.Status403Forbidden,
                Success = false,
                Message = "Authenticated user not found."
            });
        }

        if (user.UserType != UserType.PropertyOwner)
        {
            await _accessLogService.LogAsync(
                User,
                "GetOwnerBookings",
                false,
                "Only parking owners can view owner booking history");

            return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
            {
                Code = StatusCodes.Status403Forbidden,
                Success = false,
                Message = "Only parking owners can view booking history for their parking spots."
            });
        }

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

                if (requestedSpotOwnerId.Value != user.UserId)
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
                .Where(booking => booking.ParkingSpot.OwnerId == user.UserId);

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
                TotalCount = data.Count,
                Data = data
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error retrieving owner bookings for user {UserId}",
                user.UserId);
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
    /// Returns full booking, renter, vehicle, pricing, and transaction details only when the
    /// authenticated user owns the booked parking spot. Wallet and idempotency data are excluded.
    /// </summary>
    [HttpGet("{bookingId:int}")]
    [ProducesResponseType(typeof(OwnerBookingDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<OwnerBookingDetailResponse>> GetOwnerBookingById(int bookingId)
    {

        var user = await _currentUser.GetCurrentUserAsync();
        if (user == null)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
            {
                Code = StatusCodes.Status403Forbidden,
                Success = false,
                Message = "Authenticated user not found."
            });
        }

        try
        {
            var booking = await _context.Bookings
                .AsNoTracking()
                .Include(item => item.ParkingSpot)
                .Include(item => item.Renter)
                .Include(item => item.Vehicle)
                .Include(item => item.Transactions)
                .FirstOrDefaultAsync(item => item.BookingId == bookingId);

            if (booking == null)
            {
                await _accessLogService.LogAsync(
                    User,
                    "GetOwnerBookingById",
                    false,
                    $"Booking not found (id={bookingId})");

                return NotFound(new ErrorResponse
                {
                    Code = StatusCodes.Status404NotFound,
                    Success = false,
                    Message = "Booking not found."
                });
            }

            if (booking.ParkingSpot.OwnerId != user.UserId)
            {
                await _accessLogService.LogAsync(
                    User,
                    "GetOwnerBookingById",
                    false,
                    $"Not owner (bookingId={bookingId})",
                    bookingId);

                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "You are not authorized to view this booking."
                });
            }

            await _accessLogService.LogAsync(
                User,
                "GetOwnerBookingById",
                true,
                $"BookingId={bookingId}",
                bookingId);

            return Ok(new OwnerBookingDetailResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Owner booking detail retrieved successfully.",
                Data = MapOwnerBookingDetail(booking)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error retrieving owner booking {BookingId} for user {UserId}",
                bookingId,
                user.UserId);
            await _accessLogService.LogAsync(User, "GetOwnerBookingById", false, ex.Message, bookingId);

            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while retrieving the owner booking detail."
            });
        }
    }

    /// <summary>
    /// take request parameter status and parse it to BookingStatus enum, return true if parsing is successful, false otherwise.
    /// </summary>
    private static bool TryParseBookingStatus(string? status, out BookingStatus? bookingStatus)
    {
        bookingStatus = null;
        if (string.IsNullOrWhiteSpace(status))
        {
            return true;
        }

        // fetch booking status enum names and compare with the input status string, ignoring case and whitespace
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
        var (startDate, endDate, bookedDays) = GetInclusiveBookingPeriod(booking);

        return new OwnerBookingSummaryResponse
        {
            BookingId = booking.BookingId,
            BookingReference = booking.BookingReference,
            ParkingSpotId = booking.ParkingSpotId,
            ParkingLabel = booking.ParkingSpot.ParkingLabel,
            RenterId = booking.RenterId,
            RenterName = $"{booking.Renter.FirstName} {booking.Renter.LastName}".Trim(),
            RenterEmail = booking.Renter.Email,
            RenterPhoneNumber = booking.Renter.PhoneNumber,
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

    /// <summary>
    /// Maps an owner-authorized booking to the detailed response without exposing wallet identifiers,
    /// booking quote identifiers, or the renter's idempotency key.
    /// </summary>
    private static OwnerBookingDetailDataResponse MapOwnerBookingDetail(Booking booking)
    {
        var (startDate, endDate, bookedDays) = GetInclusiveBookingPeriod(booking);
        var renterFirstName = booking.Renter.FirstName ?? string.Empty;
        var renterLastName = booking.Renter.LastName ?? string.Empty;

        return new OwnerBookingDetailDataResponse
        {
            BookingId = booking.BookingId,
            BookingReference = booking.BookingReference,
            ParkingSpotId = booking.ParkingSpotId,
            ParkingLabel = booking.ParkingSpot.ParkingLabel,
            Renter = new OwnerBookingRenterResponse
            {
                RenterId = booking.RenterId,
                FirstName = renterFirstName,
                LastName = renterLastName,
                FullName = $"{renterFirstName} {renterLastName}".Trim(),
                Email = booking.Renter.Email,
                PhoneNumber = booking.Renter.PhoneNumber
            },
            Vehicle = new OwnerBookingVehicleResponse
            {
                VehicleId = booking.VehicleId,
                NumberPlate = booking.Vehicle.NumberPlate,
                Brand = booking.Vehicle.VehicleBrand,
                Model = booking.Vehicle.VehicleModel,
                Color = booking.Vehicle.VehicleColor
            },
            StartDate = startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            EndDate = endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            BookedDays = bookedDays,
            BookingStatus = booking.BookingStatus.ToString(),
            CancellationReason = booking.CancellationReason,
            CancelledAt = booking.CancelledAt,
            CheckedInAt = booking.CheckedInAt,
            ActualExitAt = booking.ActualExitAt,
            Financial = new OwnerBookingFinancialResponse
            {
                RateType = booking.RateType.ToString(),
                RatePerDaySnapshot = booking.RatePerDaySnapshot,
                RentalSubtotal = booking.RentalSubtotal,
                RenterTotal = booking.TotalAmount,
                PlatformCommissionRate = booking.PlatformCommissionRate,
                PlatformCommissionAmount = booking.PlatformCommissionAmount,
                OwnerPayoutAmount = booking.OwnerPayoutAmount,
                RefundAmount = booking.RefundAmount,
                OverstayHours = booking.OverstayHours,
                OverstayPenaltyAmount = booking.OverstayPenaltyAmount
            },
            Transactions = booking.Transactions
                .OrderBy(transaction => transaction.CreatedAt)
                .ThenBy(transaction => transaction.TransactionId)
                .Select(transaction => new OwnerBookingTransactionResponse
                {
                    TransactionId = transaction.TransactionId,
                    TransactionType = transaction.TransactionType.ToString(),
                    Amount = transaction.Amount,
                    PaymentMethod = transaction.PaymentMethod.ToString(),
                    TransactionStatus = transaction.TransactionStatus.ToString(),
                    ReferenceNumber = transaction.ReferenceNumber,
                    CreatedAt = transaction.CreatedAt,
                    UpdatedAt = transaction.UpdatedAt
                })
                .ToList(),
            CreatedAt = booking.CreatedAt,
            UpdatedAt = booking.UpdatedAt
        };
    }

    /// <summary>
    /// Converts the internal exclusive booking end boundary into inclusive customer-facing dates
    /// and falls back to a calculated day count for legacy bookings without a stored snapshot.
    /// </summary>
    private static (DateOnly StartDate, DateOnly EndDate, int BookedDays) GetInclusiveBookingPeriod(Booking booking)
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

        return (startDate, endDate, bookedDays);
    }
}
