using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;
using System.Security.Claims;

namespace ParkJomV2.Controllers;

[ApiController]
[Route("api/parking")]
public class ParkingBookingController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ParkingBookingController> _logger;

    public ParkingBookingController(ApplicationDbContext context, ILogger<ParkingBookingController> logger)
    {
        _context = context;
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
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "User not found"
                });
            }

            if (user.UserType != UserType.Renter && user.UserType != UserType.PropertyOwner)
            {
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
                return NotFound(new ErrorResponse
                {
                    Code = StatusCodes.Status404NotFound,
                    Success = false,
                    Message = "Parking spot not found"
                });
            }

            if (!spot.IsPublished || spot.AvailabilityStatus != AvailabilityStatus.Available)
            {
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
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "Parking spot is not yet verified"
                });
            }

            if (request.StartDate >= request.EndDate)
            {
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "Start date must be before end date"
                });
            }

            if (request.StartDate < DateTime.UtcNow)
            {
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
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "You are not authorized to view this booking"
                });
            }

            _logger.LogInformation("Retrieved booking {BookingId} for user {UserId}", id, userId);

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
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "You are not authorized to cancel this booking"
                });
            }

            if (booking.BookingStatus == BookingStatus.Cancelled)
            {
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "Booking is already cancelled"
                });
            }

            if (booking.BookingStatus == BookingStatus.Completed)
            {
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
