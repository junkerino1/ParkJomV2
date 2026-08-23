using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Services;
using ParkJomV2.Models.Enums;
using System.Security.Claims;

namespace ParkJomV2.Controllers;

[ApiController]
[Route("api/parking")]
public class ParkingBookingController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly AccessLogService _accessLogService;
    private readonly ILogger<ParkingBookingController> _logger;

    public ParkingBookingController(ApplicationDbContext context, AccessLogService accessLogService, ILogger<ParkingBookingController> logger)
    {
        _context = context;
        _accessLogService = accessLogService;
        _logger = logger;
    }

    /// <summary>
    /// Create a new booking for a parking spot (Renter)
    /// </summary>
    [Authorize]
    [HttpPost("bookings")]
    [ProducesResponseType(typeof(BookingDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<BookingDetailResponse>> CreateBooking([FromBody] CreateBookingRequest request)
    {
        try
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);

            if (user == null)
            {
                await _accessLogService.LogAsync(User, "CreateBooking", false, "User not found");
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "User not found"
                });
            }

            if (user.UserType != UserType.Renter && user.UserType != UserType.PropertyOwner)
            {
                await _accessLogService.LogAsync(User, "CreateBooking", false, "User is not a renter/owner");
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "Only registered users can create bookings"
                });
            }

            var spot = await _context.ParkingSpots
                .Include(ps => ps.VerificationRequests.Where(vr => vr.IsCurrent))
                .FirstOrDefaultAsync(ps => ps.ParkingSpotId == request.ParkingSpotId);

            if (spot == null)
            {
                await _accessLogService.LogAsync(User, "CreateBooking", false, "Parking spot not found");
                return NotFound(new ErrorResponse
                {
                    Code = StatusCodes.Status404NotFound,
                    Success = false,
                    Message = "Parking spot not found"
                });
            }

            if (!spot.IsPublished || spot.AvailabilityStatus != AvailabilityStatus.Available)
            {
                await _accessLogService.LogAsync(User, "CreateBooking", false, "Parking spot not available");
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "Parking spot is not available for booking"
                });
            }

            var isVerified = spot.VerificationRequests.Any(vr => vr.VerificationStatus == VerificationStatus.Approved);
            if (!isVerified)
            {
                await _accessLogService.LogAsync(User, "CreateBooking", false, "Parking spot not verified");
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "Parking spot is not yet verified"
                });
            }

            if (request.StartDate >= request.EndDate)
            {
                await _accessLogService.LogAsync(User, "CreateBooking", false, "Start date must be before end date");
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "Start date must be before end date"
                });
            }

            if (request.StartDate < DateTime.UtcNow)
            {
                await _accessLogService.LogAsync(User, "CreateBooking", false, "Start date cannot be in the past");
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "Start date cannot be in the past"
                });
            }

            // Check for overlapping bookings
            var hasOverlap = await _context.Bookings
                .AnyAsync(b => b.ParkingSpotId == request.ParkingSpotId
                    && b.BookingStatus == BookingStatus.Confirmed
                    && b.StartDate < request.EndDate
                    && b.EndDate > request.StartDate);

            if (hasOverlap)
            {
                await _accessLogService.LogAsync(User, "CreateBooking", false, "Parking spot already booked for the period");
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "Parking spot is already booked for the selected time period"
                });
            }

            var vehicle = await _context.Vehicles
                .FirstOrDefaultAsync(v => v.VehicleId == request.VehicleId && v.UserId == userId);

            if (vehicle == null)
            {
                await _accessLogService.LogAsync(User, "CreateBooking", false, "Vehicle not found or not owned by user");
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "Vehicle not found or does not belong to you"
                });
            }

            var days = (decimal)(request.EndDate - request.StartDate).TotalDays;
            var totalAmount = spot.DailyRate.HasValue
                ? spot.DailyRate.Value * days
                : (spot.MonthlyRate.HasValue
                    ? spot.MonthlyRate.Value * Math.Ceiling(days / 30)
                    : 0);

            var booking = new Booking
            {
                BookingReference = GenerateBookingReference(),
                RenterId = userId,
                ParkingSpotId = request.ParkingSpotId,
                VehicleId = request.VehicleId,
                StartDate = request.StartDate,
                EndDate = request.EndDate,
                BookingStatus = BookingStatus.Pending,
                TotalAmount = totalAmount,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Bookings.Add(booking);
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Booking created. BookingId={BookingId}, Reference={Reference}, SpotId={SpotId}, UserId={UserId}",
                booking.BookingId, booking.BookingReference, request.ParkingSpotId, userId);

            await _accessLogService.LogAsync(User, "CreateBooking", true, $"BookingId={booking.BookingId}", booking.BookingId);

            return Ok(new BookingDetailResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Booking created successfully",
                Data = MapToBookingResponseDTO(booking)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating booking");
            await _accessLogService.LogAsync(User, "CreateBooking", false, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while creating the booking"
            });
        }
    }

    /// <summary>
    /// Get all bookings for the authenticated user (as renter)
    /// </summary>
    [Authorize]
    [HttpGet("bookings/my")]
    [ProducesResponseType(typeof(BookingListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<BookingListResponse>> GetMyBookings()
    {
        try
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var bookings = await _context.Bookings
                .Include(b => b.ParkingSpot)
                .Include(b => b.Vehicle)
                .Where(b => b.RenterId == userId)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();

            var result = bookings.Select(MapToBookingResponseDTO).ToList();

            _logger.LogInformation("Retrieved {Count} bookings for user {UserId}", result.Count, userId);

            await _accessLogService.LogAsync(User, "GetMyBookings", true, $"{result.Count} booking(s)");

            return Ok(new BookingListResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = result.Count > 0
                    ? $"Retrieved {result.Count} booking(s) successfully"
                    : "No bookings found",
                Data = result
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user's bookings");
            await _accessLogService.LogAsync(User, "GetMyBookings", false, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while retrieving your bookings"
            });
        }
    }

    /// <summary>
    /// Get a specific booking by ID
    /// </summary>
    [Authorize]
    [HttpGet("bookings/{id}")]
    [ProducesResponseType(typeof(BookingDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<BookingDetailResponse>> GetBookingById(int id)
    {
        try
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);

            if (user == null)
            {
                await _accessLogService.LogAsync(User, $"GetBookingById", false, $"User not found (id={id})");
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "User not found"
                });
            }

            var booking = await _context.Bookings
                .Include(b => b.ParkingSpot)
                .Include(b => b.Vehicle)
                .Include(b => b.Renter)
                .FirstOrDefaultAsync(b => b.BookingId == id);

            if (booking == null)
            {
                await _accessLogService.LogAsync(User, "GetBookingById", false, $"Booking not found (id={id})");
                return NotFound(new ErrorResponse
                {
                    Code = StatusCodes.Status404NotFound,
                    Success = false,
                    Message = "Booking not found"
                });
            }

            // Only the renter, the spot owner, or an admin can view the booking
            if (booking.RenterId != userId &&
                booking.ParkingSpot.OwnerId != userId &&
                user.UserType != UserType.Admin)
            {
                await _accessLogService.LogAsync(User, "GetBookingById", false, $"Not authorized (id={id})");
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "You are not authorized to view this booking"
                });
            }

            _logger.LogInformation("Retrieved booking {BookingId} for user {UserId}", id, userId);

            await _accessLogService.LogAsync(User, "GetBookingById", true, $"BookingId={id}", id);

            return Ok(new BookingDetailResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Booking retrieved successfully",
                Data = MapToBookingResponseDTO(booking)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving booking {BookingId}", id);
            await _accessLogService.LogAsync(User, "GetBookingById", false, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while retrieving the booking"
            });
        }
    }

    /// <summary>
    /// Cancel a booking
    /// </summary>
    [Authorize]
    [HttpPut("bookings/{id}/cancel")]
    [ProducesResponseType(typeof(CancelBookingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<CancelBookingResponse>> CancelBooking(int id, [FromBody] CancelBookingRequest request)
    {
        try
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == userId);

            if (user == null)
            {
                await _accessLogService.LogAsync(User, "CancelBooking", false, $"User not found (id={id})");
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "User not found"
                });
            }

            var booking = await _context.Bookings
                .Include(b => b.ParkingSpot)
                .FirstOrDefaultAsync(b => b.BookingId == id);

            if (booking == null)
            {
                await _accessLogService.LogAsync(User, "CancelBooking", false, $"Booking not found (id={id})");
                return NotFound(new ErrorResponse
                {
                    Code = StatusCodes.Status404NotFound,
                    Success = false,
                    Message = "Booking not found"
                });
            }

            // Only the renter who made the booking or the spot owner can cancel
            if (booking.RenterId != userId && booking.ParkingSpot.OwnerId != userId && user.UserType != UserType.Admin)
            {
                await _accessLogService.LogAsync(User, "CancelBooking", false, $"Not authorized (id={id})");
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "You are not authorized to cancel this booking"
                });
            }

            if (booking.BookingStatus == BookingStatus.Cancelled)
            {
                await _accessLogService.LogAsync(User, "CancelBooking", false, $"Already cancelled (id={id})");
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "Booking is already cancelled"
                });
            }

            if (booking.BookingStatus == BookingStatus.Completed)
            {
                await _accessLogService.LogAsync(User, "CancelBooking", false, $"Cannot cancel completed booking (id={id})");
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "Cannot cancel a completed booking"
                });
            }

            booking.BookingStatus = BookingStatus.Cancelled;
            booking.CancellationReason = request.CancellationReason;
            booking.CancelledAt = DateTime.UtcNow;
            booking.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Booking cancelled. BookingId={BookingId}, UserId={UserId}, Reason={Reason}",
                id, userId, request.CancellationReason);

            await _accessLogService.LogAsync(User, "CancelBooking", true, $"BookingId={id}", id);

            return Ok(new CancelBookingResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Booking cancelled successfully",
                BookingId = booking.BookingId,
                BookingStatus = booking.BookingStatus.ToString(),
                CancelledAt = booking.CancelledAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling booking {BookingId}", id);
            await _accessLogService.LogAsync(User, "CancelBooking", false, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while cancelling the booking"
            });
        }
    }

    private static string GenerateBookingReference()
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var random = Random.Shared.Next(1000, 9999);
        return $"BK-{timestamp}-{random}";
    }

    private static BookingResponseDTO MapToBookingResponseDTO(Booking booking)
    {
        return new BookingResponseDTO
        {
            BookingId = booking.BookingId,
            BookingReference = booking.BookingReference,
            RenterId = booking.RenterId,
            ParkingSpotId = booking.ParkingSpotId,
            ParkingLabel = booking.ParkingSpot?.ParkingLabel,
            VehicleId = booking.VehicleId,
            StartDate = booking.StartDate,
            EndDate = booking.EndDate,
            BookingStatus = booking.BookingStatus.ToString(),
            TotalAmount = booking.TotalAmount,
            CancellationReason = booking.CancellationReason,
            CancelledAt = booking.CancelledAt,
            CreatedAt = booking.CreatedAt
        };
    }
}
