using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Services;
using ParkJomV2.Services.Pricing;

namespace ParkJomV2.Controllers;

[Route("api/owner/pricing")]
[ApiController]
[Authorize]
public class OwnerPricingController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly CurrentUserService _currentUser;
    private readonly DynamicPricingService _pricingService;
    private readonly AccessLogService _accessLog;
    private readonly ILogger<OwnerPricingController> _logger;

    public OwnerPricingController(
        ApplicationDbContext db,
        CurrentUserService currentUser,
        DynamicPricingService pricingService,
        AccessLogService accessLog,
        ILogger<OwnerPricingController> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _pricingService = pricingService;
        _accessLog = accessLog;
        _logger = logger;
    }

    /// <summary>
    /// Returns AI dynamic pricing recommendations and projected revenue uplift across all bays owned by the user.
    /// </summary>
    [HttpGet("recommendations")]
    [ProducesResponseType(typeof(OwnerPortfolioPricingResponseDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<OwnerPortfolioPricingResponseDto>> GetPortfolioRecommendations(CancellationToken ct)
    {
        var user = await _currentUser.GetCurrentUserAsync();
        if (user == null)
        {
            return Unauthorized(new ErrorResponse
            {
                Code = StatusCodes.Status401Unauthorized,
                Success = false,
                Message = "Authentication required."
            });
        }

        var result = await _pricingService.GetOwnerPortfolioRecommendationsAsync(user.UserId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Returns the detailed elasticity curve and AI pricing recommendation for a single parking spot.
    /// </summary>
    [HttpGet("spot/{parkingSpotId:int}")]
    [ProducesResponseType(typeof(SpotPricingResponseWrapperDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SpotPricingResponseWrapperDto>> GetSpotRecommendation(
        int parkingSpotId,
        [FromQuery] decimal? candidateDailyRate,
        CancellationToken ct)
    {
        var rec = await _pricingService.GetSpotRecommendationAsync(parkingSpotId, candidateDailyRate, ct);
        if (rec == null)
        {
            return NotFound(new ErrorResponse
            {
                Code = StatusCodes.Status404NotFound,
                Success = false,
                Message = $"Parking spot #{parkingSpotId} was not found."
            });
        }

        return Ok(new SpotPricingResponseWrapperDto
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "AI pricing analysis computed.",
            Data = rec
        });
    }

    /// <summary>
    /// One-click applies the AI-recommended daily and monthly rates to the owner's parking spot.
    /// </summary>
    [HttpPost("spot/{parkingSpotId:int}/apply")]
    [ProducesResponseType(typeof(SpotPricingResponseWrapperDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SpotPricingResponseWrapperDto>> ApplyRecommendedPricing(
        int parkingSpotId,
        [FromBody] ApplyDynamicPricingRequest request,
        CancellationToken ct)
    {
        var user = await _currentUser.GetCurrentUserAsync();
        if (user == null)
        {
            return Unauthorized(new ErrorResponse
            {
                Code = StatusCodes.Status401Unauthorized,
                Success = false,
                Message = "Authentication required."
            });
        }

        var spot = await _db.ParkingSpots
            .FirstOrDefaultAsync(p => p.ParkingSpotId == parkingSpotId && p.OwnerId == user.UserId, ct);

        if (spot == null)
        {
            return NotFound(new ErrorResponse
            {
                Code = StatusCodes.Status404NotFound,
                Success = false,
                Message = "Parking spot not found or does not belong to your account."
            });
        }

        spot.DailyRate = Math.Round(request.DailyRateRm, 2);
        if (request.MonthlyRateRm.HasValue && request.MonthlyRateRm.Value >= 1m)
        {
            spot.MonthlyRate = Math.Round(request.MonthlyRateRm.Value, 2);
        }
        spot.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        await _accessLog.LogAsync(
            User,
            "AI_ApplyDynamicPricing",
            true,
            $"SpotId={parkingSpotId}, DailyRate=RM {spot.DailyRate:0.00}, MonthlyRate=RM {spot.MonthlyRate:0.00}");

        var updated = await _pricingService.GetSpotRecommendationAsync(parkingSpotId, spot.DailyRate, ct);
        return Ok(new SpotPricingResponseWrapperDto
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = $"Applied AI optimal rates (RM {spot.DailyRate:0.00}/day · RM {spot.MonthlyRate:0.00}/month).",
            Data = updated!
        });
    }
}
