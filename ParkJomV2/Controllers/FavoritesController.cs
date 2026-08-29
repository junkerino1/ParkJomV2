using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services;

namespace ParkJomV2.Controllers;

[ApiController]
[Authorize]
[Route("api/favorites")]
public class FavoritesController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly CurrentUserService _currentUser;
    private readonly AccessLogService _accessLogService;
    private readonly ILogger<FavoritesController> _logger;

    public FavoritesController(
        ApplicationDbContext context,
        CurrentUserService currentUser,
        AccessLogService accessLogService,
        ILogger<FavoritesController> logger)
    {
        _context = context;
        _currentUser = currentUser;
        _accessLogService = accessLogService;
        _logger = logger;
    }

    /// <summary>Adds a published parking spot to the authenticated commuter's favorites.</summary>
    [HttpPost("{parkingSpotId:int}")]
    [ProducesResponseType(typeof(AddFavoriteResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<AddFavoriteResponse>> AddFavorite(int parkingSpotId)
    {
        try
        {
            var user = await _currentUser.GetCurrentUserAsync();
            if (user == null)
            {
                return NotFound(new ErrorResponse
                {
                    Code = StatusCodes.Status404NotFound,
                    Success = false,
                    Message = "User not found."
                });
            }

            if (user.UserType != UserType.Renter)
            {
                await _accessLogService.LogAsync(User, "AddFavorite", false, "Only commuters can add favorites");
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "Only commuters can add parking spots to favorites."
                });
            }

            var parkingSpotExists = await _context.ParkingSpots.AnyAsync(spot =>
                spot.ParkingSpotId == parkingSpotId &&
                spot.IsPublished &&
                spot.AvailabilityStatus == AvailabilityStatus.Available &&
                spot.Owner.AccountStatus != "Suspended");

            if (!parkingSpotExists)
            {
                await _accessLogService.LogAsync(User, "AddFavorite", false, $"ParkingSpotId={parkingSpotId} not found or unavailable");
                return NotFound(new ErrorResponse
                {
                    Code = StatusCodes.Status404NotFound,
                    Success = false,
                    Message = "Parking spot not found or unavailable."
                });
            }

            var alreadyExists = await _context.Favorites.AnyAsync(favorite =>
                favorite.UserId == user.UserId &&
                favorite.ParkingSpotId == parkingSpotId);

            if (alreadyExists)
            {
                await _accessLogService.LogAsync(User, "AddFavorite", false, $"ParkingSpotId={parkingSpotId} is already a favorite");
                return Conflict(new ErrorResponse
                {
                    Code = StatusCodes.Status409Conflict,
                    Success = false,
                    Message = "This parking spot is already in your favorites."
                });
            }

            var now = DateTime.UtcNow;
            var favorite = new Favorite
            {
                UserId = user.UserId,
                ParkingSpotId = parkingSpotId,
                CreatedAt = now,
                UpdatedAt = now
            };

            _context.Favorites.Add(favorite);
            await _context.SaveChangesAsync();

            await _accessLogService.LogAsync(User, "AddFavorite", true, $"FavoriteId={favorite.FavoriteId}, ParkingSpotId={parkingSpotId}");

            return StatusCode(StatusCodes.Status201Created, new AddFavoriteResponse
            {
                Code = StatusCodes.Status201Created,
                Success = true,
                Message = "Parking spot added to favorites successfully.",
                Data = new FavoriteDTO
                {
                    FavoriteId = favorite.FavoriteId,
                    ParkingSpotId = favorite.ParkingSpotId,
                    CreatedAt = favorite.CreatedAt
                }
            });
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Duplicate favorite attempted for parking spot {ParkingSpotId}", parkingSpotId);
            return Conflict(new ErrorResponse
            {
                Code = StatusCodes.Status409Conflict,
                Success = false,
                Message = "This parking spot is already in your favorites."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding parking spot {ParkingSpotId} to favorites", parkingSpotId);
            await _accessLogService.LogAsync(User, "AddFavorite", false, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while adding the parking spot to favorites."
            });
        }
    }

    /// <summary>
    /// Toggles a parking spot in the authenticated commuter's favorites.
    /// Adds it when absent and removes it when already favorited.
    /// </summary>
    [HttpPost("update/{parkingSpotId:int}")]
    [ProducesResponseType(typeof(UpdateFavoriteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<UpdateFavoriteResponse>> UpdateFavorite(int parkingSpotId)
    {
        try
        {
            var user = await _currentUser.GetCurrentUserAsync();
            if (user == null)
            {
                return NotFound(new ErrorResponse
                {
                    Code = StatusCodes.Status404NotFound,
                    Success = false,
                    Message = "User not found."
                });
            }

            if (user.UserType != UserType.Renter)
            {
                await _accessLogService.LogAsync(User, "UpdateFavorite", false, "Only commuters can update favorites");
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "Only commuters can update favorites."
                });
            }

            var favorite = await _context.Favorites.FirstOrDefaultAsync(item =>
                item.UserId == user.UserId &&
                item.ParkingSpotId == parkingSpotId);

            if (favorite != null)
            {
                _context.Favorites.Remove(favorite);
                await _context.SaveChangesAsync();
                await _accessLogService.LogAsync(User, "UpdateFavorite", true, $"Removed ParkingSpotId={parkingSpotId}");

                return Ok(new UpdateFavoriteResponse
                {
                    Code = StatusCodes.Status200OK,
                    Success = true,
                    Message = "Parking spot removed from favorites successfully.",
                    Data = new UpdateFavoriteDTO
                    {
                        ParkingSpotId = parkingSpotId,
                        IsFavorite = false
                    }
                });
            }

            var parkingSpotExists = await _context.ParkingSpots.AnyAsync(spot =>
                spot.ParkingSpotId == parkingSpotId &&
                spot.IsPublished &&
                spot.AvailabilityStatus == AvailabilityStatus.Available &&
                spot.Owner.AccountStatus != "Suspended");

            if (!parkingSpotExists)
            {
                await _accessLogService.LogAsync(User, "UpdateFavorite", false, $"ParkingSpotId={parkingSpotId} not found or unavailable");
                return NotFound(new ErrorResponse
                {
                    Code = StatusCodes.Status404NotFound,
                    Success = false,
                    Message = "Parking spot not found or unavailable."
                });
            }

            var now = DateTime.UtcNow;
            _context.Favorites.Add(new Favorite
            {
                UserId = user.UserId,
                ParkingSpotId = parkingSpotId,
                CreatedAt = now,
                UpdatedAt = now
            });
            await _context.SaveChangesAsync();
            await _accessLogService.LogAsync(User, "UpdateFavorite", true, $"Added ParkingSpotId={parkingSpotId}");

            return Ok(new UpdateFavoriteResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Parking spot added to favorites successfully.",
                Data = new UpdateFavoriteDTO
                {
                    ParkingSpotId = parkingSpotId,
                    IsFavorite = true
                }
            });
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Concurrent favorite update attempted for parking spot {ParkingSpotId}", parkingSpotId);
            return Conflict(new ErrorResponse
            {
                Code = StatusCodes.Status409Conflict,
                Success = false,
                Message = "The favorite was updated by another request. Please refresh and try again."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating favorite for parking spot {ParkingSpotId}", parkingSpotId);
            await _accessLogService.LogAsync(User, "UpdateFavorite", false, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while updating the favorite."
            });
        }
    }

    /// <summary>Gets all parking spots favorited by the authenticated commuter.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(GetFavoritesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<GetFavoritesResponse>> GetFavorites()
    {
        try
        {
            var user = await _currentUser.GetCurrentUserAsync();
            if (user == null)
            {
                return NotFound(new ErrorResponse
                {
                    Code = StatusCodes.Status404NotFound,
                    Success = false,
                    Message = "User not found."
                });
            }

            if (user.UserType != UserType.Renter)
            {
                await _accessLogService.LogAsync(User, "GetFavorites", false, "Only commuters can view favorites");
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "Only commuters can view favorites."
                });
            }

            var favorites = await _context.Favorites
                .AsNoTracking()
                .Where(favorite =>
                    favorite.UserId == user.UserId &&
                    favorite.ParkingSpot.AvailabilityStatus != AvailabilityStatus.Deleted)
                .Include(favorite => favorite.ParkingSpot)
                    .ThenInclude(spot => spot.Property)
                        .ThenInclude(property => property.Station)
                .Include(favorite => favorite.ParkingSpot)
                    .ThenInclude(spot => spot.ParkingSpotImages.Where(image => image.IsPrimary))
                        .ThenInclude(image => image.MediaFile)
                .OrderByDescending(favorite => favorite.UpdatedAt)
                .Select(favorite => new FavoriteParkingSpotDTO
                {
                    FavoriteId = favorite.FavoriteId,
                    ParkingSpotId = favorite.ParkingSpotId,
                    ParkingLabel = favorite.ParkingSpot.ParkingLabel,
                    PropertyId = favorite.ParkingSpot.PropertyId,
                    PropertyName = favorite.ParkingSpot.Property.PropertyName,
                    Address = favorite.ParkingSpot.Property.Address,
                    StationName = favorite.ParkingSpot.Property.Station.StationName,
                    DistanceToStation = favorite.ParkingSpot.Property.DistanceToStation,
                    TimeToStationInMinutes = favorite.ParkingSpot.Property.TimeToStation,
                    AvailabilityStatus = favorite.ParkingSpot.AvailabilityStatus.ToString(),
                    MonthlyRate = favorite.ParkingSpot.MonthlyRate,
                    DailyRate = favorite.ParkingSpot.DailyRate,
                    PrimaryImageUrl = favorite.ParkingSpot.ParkingSpotImages
                        .Where(image => image.IsPrimary)
                        .Select(image => image.MediaFile.SecureUrl)
                        .FirstOrDefault(),
                    FavoritedAt = favorite.CreatedAt
                })
                .ToListAsync();

            await _accessLogService.LogAsync(User, "GetFavorites", true, $"Count={favorites.Count}");

            return Ok(new GetFavoritesResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Favorites retrieved successfully.",
                TotalCount = favorites.Count,
                Data = favorites
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving favorites");
            await _accessLogService.LogAsync(User, "GetFavorites", false, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while retrieving favorites."
            });
        }
    }
}
