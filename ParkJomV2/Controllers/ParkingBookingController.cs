using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Services;
using ParkJomV2.Models.Enums;
using System.ComponentModel.DataAnnotations;

namespace ParkJomV2.Controllers;

[ApiController]
[Route("api/parking")]
public class ParkingBookingController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly CurrentUserService _currentUser;
    private readonly AccessLogService _accessLogService;
    private readonly ILogger<ParkingBookingController> _logger;

    public ParkingBookingController(ApplicationDbContext context, CurrentUserService currentUser, AccessLogService accessLogService, ILogger<ParkingBookingController> logger)
    {
        _context = context;
        _currentUser = currentUser;
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
            var user = await _currentUser.GetCurrentUserAsync();

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
                .Include(ps => ps.Owner)
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

            if (string.Equals(spot.Owner.AccountStatus, "Suspended", StringComparison.OrdinalIgnoreCase) ||
                !spot.IsPublished ||
                spot.AvailabilityStatus != AvailabilityStatus.Available)
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
                .FirstOrDefaultAsync(v => v.VehicleId == request.VehicleId && v.UserId == user.UserId);

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
                RenterId = user.UserId,
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
                booking.BookingId, booking.BookingReference, request.ParkingSpotId, user.UserId);

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
            var user = await _currentUser.GetCurrentUserAsync();

            if(user == null)
            {
                await _accessLogService.LogAsync(User, "GetMyBookings", false, "User not found");
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "User not found"
                });
            }

            var bookings = await _context.Bookings
                .Include(b => b.ParkingSpot)
                .Include(b => b.Vehicle)
                .Where(b => b.RenterId == user.UserId)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();

            var result = bookings.Select(MapToBookingResponseDTO).ToList();

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
    /// Get the authenticated commuter's completed, cancelled, and expired booking history,
    /// including any review submitted for each booking.
    /// </summary>
    [Authorize]
    [HttpGet("/api/bookings/history")]
    [ProducesResponseType(typeof(BookingHistoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<BookingHistoryResponse>> GetBookingHistory(
        [FromQuery, Range(1, 1_000_000)] int page = 1,
        [FromQuery, Range(1, 50)] int pageSize = 10)
    {
        try
        {
            var user = await _currentUser.GetCurrentUserAsync();

            if (user == null)
            {
                await _accessLogService.LogAsync(User, "GetBookingHistory", false, "User not found");
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "User not found"
                });
            }

            if (user.UserType != UserType.Renter)
            {
                await _accessLogService.LogAsync(User, "GetBookingHistory", false, "Only commuters can view booking history");
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "Only commuters can view booking history"
                });
            }

            var historyQuery = _context.Bookings
                .AsNoTracking()
                .Where(booking => booking.RenterId == user.UserId &&
                    (booking.BookingStatus == BookingStatus.Completed ||
                     booking.BookingStatus == BookingStatus.Cancelled ||
                     booking.BookingStatus == BookingStatus.Expired));

            var totalCount = await historyQuery.CountAsync();
            var bookings = await historyQuery
                .Include(booking => booking.ParkingSpot)
                .Include(booking => booking.Vehicle)
                .Include(booking => booking.Reviews.Where(review => review.ReviewerId == user.UserId))
                    .ThenInclude(review => review.Reviewer)
                .OrderByDescending(booking => booking.EndDate)
                .ThenByDescending(booking => booking.BookingId)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .AsSplitQuery()
                .ToListAsync();

            var result = bookings.Select(MapToBookingHistoryItemDTO).ToList();
            await _accessLogService.LogAsync(
                User,
                "GetBookingHistory",
                true,
                $"Page={page}, PageSize={pageSize}, Returned={result.Count}, Total={totalCount}");

            return Ok(new BookingHistoryResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = result.Count > 0
                    ? "Booking history retrieved successfully"
                    : "No booking history found",
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = totalCount == 0
                    ? 0
                    : (int)Math.Ceiling(totalCount / (double)pageSize),
                Data = result
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving commuter booking history");
            await _accessLogService.LogAsync(User, "GetBookingHistory", false, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while retrieving your booking history"
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
            var user = await _currentUser.GetCurrentUserAsync();

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
            if (booking.RenterId != user.UserId &&
                booking.ParkingSpot.OwnerId != user.UserId &&
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
            var user = await _currentUser.GetCurrentUserAsync();

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
            if (booking.RenterId != user.UserId && booking.ParkingSpot.OwnerId != user.UserId && user.UserType != UserType.Admin)
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
                id, user.UserId, request.CancellationReason);

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

    private static BookingHistoryItemDTO MapToBookingHistoryItemDTO(Booking booking)
    {
        var review = booking.Reviews.SingleOrDefault();

        return new BookingHistoryItemDTO
        {
            Booking = MapToBookingResponseDTO(booking),
            CanReview = booking.BookingStatus == BookingStatus.Completed && review == null,
            Review = review == null ? null : MapToReviewDTO(review, booking)
        };
    }

    private static ReviewDTO MapToReviewDTO(Review review, Booking booking)
    {
        var firstName = review.Reviewer.FirstName?.Trim();
        var lastInitial = string.IsNullOrWhiteSpace(review.Reviewer.LastName)
            ? string.Empty
            : $" {char.ToUpperInvariant(review.Reviewer.LastName.Trim()[0])}.";

        return new ReviewDTO
        {
            ReviewId = review.ReviewId,
            ParkingSpotId = review.ParkingSpotId,
            Rating = review.Rating,
            Comment = review.Comment,
            ReviewerDisplayName = string.IsNullOrWhiteSpace(firstName)
                ? "ParkJom commuter"
                : firstName + lastInitial,
            IsVerifiedBooking = booking.RenterId == review.ReviewerId &&
                                booking.BookingStatus == BookingStatus.Completed,
            OwnerReply = review.OwnerReply,
            OwnerReplyAt = review.OwnerReplyAt,
            CreatedAt = review.CreatedAt,
            UpdatedAt = review.UpdatedAt
        };
    }
}
