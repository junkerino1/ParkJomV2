using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services;
using System.ComponentModel.DataAnnotations;

namespace ParkJomV2.Controllers;

[ApiController]
[Route("api/reviews")]
public class ReviewsController : ControllerBase
{
    private const int MaximumPageSize = 50;
    private readonly ApplicationDbContext _context;
    private readonly CurrentUserService _currentUser;
    private readonly AccessLogService _accessLogService;
    private readonly ILogger<ReviewsController> _logger;

    public ReviewsController(
        ApplicationDbContext context,
        CurrentUserService currentUser,
        AccessLogService accessLogService,
        ILogger<ReviewsController> logger)
    {
        _context = context;
        _currentUser = currentUser;
        _accessLogService = accessLogService;
        _logger = logger;
    }

    /// <summary>Creates a review for an authenticated commuter's completed booking.</summary>
    [Authorize]
    [HttpPost]
    [ProducesResponseType(typeof(ReviewResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReviewResponse>> CreateReview([FromBody] CreateReviewRequest request)
    {
        try
        {
            var user = await _currentUser.GetCurrentUserAsync();
            if (user == null)
            {
                return NotFound(Error(StatusCodes.Status404NotFound, "User not found."));
            }

            if (user.UserType != UserType.Renter)
            {
                await _accessLogService.LogAsync(User, "CreateReview", false, "Only commuters can create reviews");
                return StatusCode(StatusCodes.Status403Forbidden,
                    Error(StatusCodes.Status403Forbidden, "Only commuters can create reviews."));
            }

            var booking = await _context.Bookings
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.BookingId == request.BookingId);

            if (booking == null)
            {
                return NotFound(Error(StatusCodes.Status404NotFound, "Booking not found."));
            }

            if (booking.RenterId != user.UserId)
            {
                await _accessLogService.LogAsync(User, "CreateReview", false, $"BookingId={request.BookingId} belongs to another commuter");
                return StatusCode(StatusCodes.Status403Forbidden,
                    Error(StatusCodes.Status403Forbidden, "You can only review your own booking."));
            }

            if (booking.BookingStatus != BookingStatus.Completed)
            {
                return Conflict(Error(StatusCodes.Status409Conflict,
                    "A review can only be created after the booking is completed."));
            }

            if (await _context.Reviews.AnyAsync(item => item.BookingId == booking.BookingId))
            {
                return Conflict(Error(StatusCodes.Status409Conflict,
                    "A review has already been submitted for this booking."));
            }

            var now = DateTime.UtcNow;
            var review = new Review
            {
                BookingId = booking.BookingId,
                ParkingSpotId = booking.ParkingSpotId,
                ReviewerId = user.UserId,
                Rating = request.Rating,
                Comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim(),
                CreatedAt = now,
                UpdatedAt = now
            };

            _context.Reviews.Add(review);
            await _context.SaveChangesAsync();
            await _accessLogService.LogAsync(User, "CreateReview", true,
                $"ReviewId={review.ReviewId}, BookingId={booking.BookingId}");

            var createdReview = await ReviewQuery()
                .SingleAsync(item => item.ReviewId == review.ReviewId);

            return CreatedAtAction(nameof(GetReview), new { reviewId = review.ReviewId }, new ReviewResponse
            {
                Code = StatusCodes.Status201Created,
                Success = true,
                Message = "Review published successfully.",
                Data = ToDto(createdReview)
            });
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Duplicate review attempted for booking {BookingId}", request.BookingId);
            return Conflict(Error(StatusCodes.Status409Conflict,
                "A review has already been submitted for this booking."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating review for booking {BookingId}", request.BookingId);
            await _accessLogService.LogAsync(User, "CreateReview", false, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError,
                Error(StatusCodes.Status500InternalServerError, "An error occurred while creating the review."));
        }
    }

    /// <summary>Gets one review for an authenticated commuter.</summary>
    [Authorize]
    [HttpGet("{reviewId:int}")]
    [ProducesResponseType(typeof(ReviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReviewResponse>> GetReview(int reviewId)
    {
        var user = await _currentUser.GetCurrentUserAsync();
        if (user == null || user.UserType != UserType.Renter)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                Error(StatusCodes.Status403Forbidden, "Only commuters can view reviews through this endpoint."));
        }

        var review = await ReviewQuery()
            .FirstOrDefaultAsync(item => item.ReviewId == reviewId);

        if (review == null)
        {
            return NotFound(Error(StatusCodes.Status404NotFound, "Review not found."));
        }

        return Ok(new ReviewResponse
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Review retrieved successfully.",
            Data = ToDto(review)
        });
    }

    /// <summary>Gets paginated reviews and rating summary for a parking spot for an authenticated commuter.</summary>
    [Authorize]
    [HttpGet("parking/{parkingSpotId:int}")]
    [ProducesResponseType(typeof(ParkingReviewsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ParkingReviewsResponse>> GetParkingReviews(
        int parkingSpotId,
        [FromQuery, Range(1, 1_000_000)] int page = 1,
        [FromQuery, Range(1, MaximumPageSize)] int pageSize = 10)
    {
        var user = await _currentUser.GetCurrentUserAsync();
        if (user == null || user.UserType != UserType.Renter)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                Error(StatusCodes.Status403Forbidden, "Only commuters can view reviews through this endpoint."));
        }

        var parkingSpotExists = await _context.ParkingSpots
            .AsNoTracking()
            .AnyAsync(spot => spot.ParkingSpotId == parkingSpotId);

        if (!parkingSpotExists)
        {
            return NotFound(Error(StatusCodes.Status404NotFound, "Parking spot not found."));
        }

        var baseQuery = _context.Reviews
            .AsNoTracking()
            .Where(review => review.ParkingSpotId == parkingSpotId);

        var totalCount = await baseQuery.CountAsync();
        var averageRating = totalCount == 0
            ? 0
            : Math.Round(await baseQuery.AverageAsync(review => (double)review.Rating), 1);

        var reviews = await ReviewQuery()
            .Where(review => review.ParkingSpotId == parkingSpotId)
            .OrderByDescending(review => review.CreatedAt)
            .ThenByDescending(review => review.ReviewId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new ParkingReviewsResponse
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Parking reviews retrieved successfully.",
            ParkingSpotId = parkingSpotId,
            TotalCount = totalCount,
            AverageRating = averageRating,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize),
            Data = reviews.Select(ToDto).ToList()
        });
    }

    /// <summary>Gets paginated reviews for one parking spot owned by the authenticated owner.</summary>
    [Authorize]
    [HttpGet("owner/parking/{parkingSpotId:int}")]
    [ProducesResponseType(typeof(ReviewListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReviewListResponse>> GetOwnerParkingReviews(
        int parkingSpotId,
        [FromQuery, Range(1, 1_000_000)] int page = 1,
        [FromQuery, Range(1, MaximumPageSize)] int pageSize = 10)
    {
        var user = await _currentUser.GetCurrentUserAsync();
        if (user == null || user.UserType != UserType.PropertyOwner)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                Error(StatusCodes.Status403Forbidden, "Only parking owners can view owner reviews."));
        }

        var ownsParkingSpot = await _context.ParkingSpots
            .AsNoTracking()
            .AnyAsync(spot => spot.ParkingSpotId == parkingSpotId && spot.OwnerId == user.UserId);

        if (!ownsParkingSpot)
        {
            return NotFound(Error(StatusCodes.Status404NotFound, "Parking spot not found."));
        }

        var baseQuery = _context.Reviews
            .AsNoTracking()
            .Where(review => review.ParkingSpotId == parkingSpotId);

        var totalCount = await baseQuery.CountAsync();
        var reviews = await ReviewQuery()
            .Where(review => review.ParkingSpotId == parkingSpotId)
            .OrderByDescending(review => review.CreatedAt)
            .ThenByDescending(review => review.ReviewId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        await _accessLogService.LogAsync(User, "GetOwnerParkingReviews", true,
            $"ParkingSpotId={parkingSpotId}, Page={page}, PageSize={pageSize}, Returned={reviews.Count}");

        return Ok(CreateReviewListResponse(reviews, totalCount, page, pageSize,
            "Parking reviews retrieved successfully."));
    }

    /// <summary>Gets paginated reviews for one parking spot for administrator moderation.</summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpGet("admin/parking/{parkingSpotId:int}")]
    [ProducesResponseType(typeof(ReviewListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReviewListResponse>> GetAdminParkingReviews(
        int parkingSpotId,
        [FromQuery, Range(1, 1_000_000)] int page = 1,
        [FromQuery, Range(1, MaximumPageSize)] int pageSize = 10)
    {
        var parkingSpotExists = await _context.ParkingSpots
            .AsNoTracking()
            .AnyAsync(spot => spot.ParkingSpotId == parkingSpotId);

        if (!parkingSpotExists)
        {
            return NotFound(Error(StatusCodes.Status404NotFound, "Parking spot not found."));
        }

        var baseQuery = _context.Reviews
            .AsNoTracking()
            .Where(review => review.ParkingSpotId == parkingSpotId);

        var totalCount = await baseQuery.CountAsync();
        var reviews = await ReviewQuery()
            .Where(review => review.ParkingSpotId == parkingSpotId)
            .OrderByDescending(review => review.CreatedAt)
            .ThenByDescending(review => review.ReviewId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        await _accessLogService.LogAsync(User, "GetAdminParkingReviews", true,
            $"ParkingSpotId={parkingSpotId}, Page={page}, PageSize={pageSize}, Returned={reviews.Count}");

        return Ok(CreateReviewListResponse(reviews, totalCount, page, pageSize,
            "Parking reviews retrieved successfully."));
    }

    /// <summary>Updates the authenticated reviewer's rating and comment.</summary>
    [Authorize]
    [HttpPut("{reviewId:int}")]
    [ProducesResponseType(typeof(ReviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReviewResponse>> UpdateReview(
        int reviewId,
        [FromBody] UpdateReviewRequest request)
    {
        try
        {
            var user = await _currentUser.GetCurrentUserAsync();
            if (user == null)
            {
                return NotFound(Error(StatusCodes.Status404NotFound, "User not found."));
            }

            var review = await _context.Reviews
                .Include(item => item.Reviewer)
                .Include(item => item.Booking)
                .FirstOrDefaultAsync(item => item.ReviewId == reviewId);

            if (review == null)
            {
                return NotFound(Error(StatusCodes.Status404NotFound, "Review not found."));
            }

            if (review.ReviewerId != user.UserId)
            {
                await _accessLogService.LogAsync(User, "UpdateReview", false, $"ReviewId={reviewId} belongs to another reviewer");
                return StatusCode(StatusCodes.Status403Forbidden,
                    Error(StatusCodes.Status403Forbidden, "You can only update your own review."));
            }

            review.Rating = request.Rating;
            review.Comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
            review.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            await _accessLogService.LogAsync(User, "UpdateReview", true, $"ReviewId={reviewId}");

            return Ok(new ReviewResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Review updated successfully.",
                Data = ToDto(review)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating review {ReviewId}", reviewId);
            await _accessLogService.LogAsync(User, "UpdateReview", false, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError,
                Error(StatusCodes.Status500InternalServerError, "An error occurred while updating the review."));
        }
    }

    /// <summary>Creates or updates the parking owner's reply to a specific review.</summary>
    [Authorize]
    [HttpPut("{reviewId:int}/owner-reply")]
    [ProducesResponseType(typeof(ReviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReviewResponse>> UpsertOwnerReply(
        int reviewId,
        [FromBody] OwnerReplyRequest request)
    {
        try
        {
            var user = await _currentUser.GetCurrentUserAsync();
            if (user == null)
            {
                return NotFound(Error(StatusCodes.Status404NotFound, "User not found."));
            }

            if (user.UserType != UserType.PropertyOwner)
            {
                await _accessLogService.LogAsync(User, "UpsertOwnerReply", false,
                    "Only parking owners can reply to reviews");
                return StatusCode(StatusCodes.Status403Forbidden,
                    Error(StatusCodes.Status403Forbidden, "Only parking owners can reply to reviews."));
            }

            if (string.IsNullOrWhiteSpace(request.OwnerReply))
            {
                return BadRequest(Error(StatusCodes.Status400BadRequest, "Owner reply cannot be empty."));
            }

            var review = await _context.Reviews
                .Include(item => item.Reviewer)
                .Include(item => item.Booking)
                .Include(item => item.ParkingSpot)
                .FirstOrDefaultAsync(item => item.ReviewId == reviewId);

            if (review == null)
            {
                return NotFound(Error(StatusCodes.Status404NotFound, "Review not found."));
            }

            if (review.ParkingSpot.OwnerId != user.UserId)
            {
                await _accessLogService.LogAsync(User, "UpsertOwnerReply", false,
                    $"ReviewId={reviewId} is for another owner's parking spot");
                return StatusCode(StatusCodes.Status403Forbidden,
                    Error(StatusCodes.Status403Forbidden,
                        "You can only reply to reviews for your own parking spots."));
            }

            var now = DateTime.UtcNow;
            review.OwnerReply = request.OwnerReply.Trim();
            review.OwnerReplyAt = now;
            review.UpdatedAt = now;
            await _context.SaveChangesAsync();

            await _accessLogService.LogAsync(User, "UpsertOwnerReply", true,
                $"ReviewId={reviewId}, ParkingSpotId={review.ParkingSpotId}");

            return Ok(new ReviewResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Owner reply published successfully.",
                Data = ToDto(review)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing owner reply for review {ReviewId}", reviewId);
            await _accessLogService.LogAsync(User, "UpsertOwnerReply", false, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError,
                Error(StatusCodes.Status500InternalServerError,
                    "An error occurred while publishing the owner reply."));
        }
    }

    /// <summary>Deletes a review owned by its author, or deletes any review as an administrator.</summary>
    [Authorize]
    [HttpDelete("{reviewId:int}")]
    [ProducesResponseType(typeof(DeleteReviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DeleteReviewResponse>> DeleteReview(int reviewId)
    {
        try
        {
            var user = await _currentUser.GetCurrentUserAsync();
            if (user == null)
            {
                return NotFound(Error(StatusCodes.Status404NotFound, "User not found."));
            }

            var review = await _context.Reviews.FindAsync(reviewId);
            if (review == null)
            {
                return NotFound(Error(StatusCodes.Status404NotFound, "Review not found."));
            }

            var isReviewOwner = review.ReviewerId == user.UserId;
            var isAdmin = user.UserType == UserType.Admin;

            if (!isReviewOwner && !isAdmin)
            {
                await _accessLogService.LogAsync(User, "DeleteReview", false, $"ReviewId={reviewId} belongs to another reviewer");
                return StatusCode(StatusCodes.Status403Forbidden,
                    Error(StatusCodes.Status403Forbidden, "You can only delete your own review unless you are an administrator."));
            }

            _context.Reviews.Remove(review);
            await _context.SaveChangesAsync();
            var deletionType = isAdmin && !isReviewOwner ? "Admin moderation" : "Reviewer deletion";
            await _accessLogService.LogAsync(User, "DeleteReview", true, $"ReviewId={reviewId}, Type={deletionType}");

            return Ok(new DeleteReviewResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Review deleted successfully."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting review {ReviewId}", reviewId);
            await _accessLogService.LogAsync(User, "DeleteReview", false, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError,
                Error(StatusCodes.Status500InternalServerError, "An error occurred while deleting the review."));
        }
    }

    /// <summary>Deletes a review for a parking spot as an administrator.</summary>
    [Authorize(Policy = "AdminOnly")]
    [HttpDelete("admin/parking/{parkingSpotId:int}/{reviewId:int}")]
    [ProducesResponseType(typeof(DeleteReviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public Task<ActionResult<DeleteReviewResponse>> DeleteAdminParkingReview(int parkingSpotId, int reviewId) =>
        DeleteReviewForParkingSpot(parkingSpotId, reviewId);

    private IQueryable<Review> ReviewQuery() => _context.Reviews
        .AsNoTracking()
        .Include(review => review.Reviewer)
        .Include(review => review.Booking)
        .Include(review => review.ParkingSpot);

    private async Task<ActionResult<DeleteReviewResponse>> DeleteReviewForParkingSpot(
        int parkingSpotId,
        int reviewId)
    {
        try
        {
            var user = await _currentUser.GetCurrentUserAsync();
            if (user == null || user.UserType != UserType.Admin)
            {
                return StatusCode(StatusCodes.Status403Forbidden,
                    Error(StatusCodes.Status403Forbidden, "You are not authorized to delete this review."));
            }

            var review = await _context.Reviews
                .Include(item => item.ParkingSpot)
                .FirstOrDefaultAsync(item => item.ReviewId == reviewId && item.ParkingSpotId == parkingSpotId);

            if (review == null)
            {
                return NotFound(Error(StatusCodes.Status404NotFound,
                    "Review not found for the specified parking spot."));
            }

            _context.Reviews.Remove(review);
            await _context.SaveChangesAsync();

            await _accessLogService.LogAsync(User, "DeleteAdminParkingReview", true,
                $"ReviewId={reviewId}, ParkingSpotId={parkingSpotId}");

            return Ok(new DeleteReviewResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Review deleted successfully."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting review {ReviewId} for parking spot {ParkingSpotId}",
                reviewId, parkingSpotId);
            await _accessLogService.LogAsync(User, "DeleteReviewForParkingSpot", false, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError,
                Error(StatusCodes.Status500InternalServerError, "An error occurred while deleting the review."));
        }
    }

    private static ReviewListResponse CreateReviewListResponse(
        List<Review> reviews,
        int totalCount,
        int page,
        int pageSize,
        string message) => new()
    {
        Code = StatusCodes.Status200OK,
        Success = true,
        Message = message,
        TotalCount = totalCount,
        Page = page,
        PageSize = pageSize,
        TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize),
        Data = reviews.Select(ToDto).ToList()
    };

    private static ReviewDTO ToDto(Review review)
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
            ReviewerDisplayName = string.IsNullOrWhiteSpace(firstName) ? "ParkJom commuter" : firstName + lastInitial,
            IsVerifiedBooking = review.Booking.RenterId == review.ReviewerId &&
                                review.Booking.BookingStatus == BookingStatus.Completed,
            OwnerReply = review.OwnerReply,
            OwnerReplyAt = review.OwnerReplyAt,
            CreatedAt = review.CreatedAt,
            UpdatedAt = review.UpdatedAt
        };
    }

    private static ErrorResponse Error(int code, string message) => new()
    {
        Code = code,
        Success = false,
        Message = message
    };
}
