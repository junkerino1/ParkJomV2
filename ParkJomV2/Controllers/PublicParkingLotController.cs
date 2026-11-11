using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkJomV2.DTOs;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services;

namespace ParkJomV2.Controllers;

[ApiController]
[Route("api/public-parking")]
public class PublicParkingLotController : ControllerBase
{
    private readonly PublicParkingService _publicParkingService;
    private readonly CurrentUserService _currentUser;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PublicParkingLotController> _logger;

    public PublicParkingLotController(
        PublicParkingService publicParkingService,
        CurrentUserService currentUser,
        IConfiguration configuration,
        ILogger<PublicParkingLotController> logger)
    {
        _publicParkingService = publicParkingService;
        _currentUser = currentUser;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// List all public parking lots with real-time remaining bays and opening status.
    /// </summary>
    [HttpGet("lots")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(List<PublicParkingLotDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<PublicParkingLotDto>>> GetLots(CancellationToken ct)
    {
        var lots = await _publicParkingService.GetLotsAsync(ct);
        return Ok(lots);
    }

    /// <summary>
    /// Get details of a single public parking lot.
    /// </summary>
    [HttpGet("lots/{id:int}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PublicParkingLotDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PublicParkingLotDto>> GetLot(int id, CancellationToken ct)
    {
        var lot = await _publicParkingService.GetLotByIdAsync(id, ct);
        if (lot == null) return NotFound(new ErrorResponse { Code = 404, Success = false, Message = "Public parking lot not found." });
        return Ok(lot);
    }

    /// <summary>
    /// Drive-in entrance plate scan. Creates an active parking session and deducts available bays.
    /// No booking required.
    /// </summary>
    [HttpPost("entry")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PublicParkingSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PublicParkingSessionDto>> RecordEntry(
        [FromBody] PublicParkingEntryRequest request,
        CancellationToken ct)
    {
        try
        {
            var user = await _currentUser.GetCurrentUserAsync();
            var session = await _publicParkingService.RecordEntryAsync(request, user?.UserId, ct);
            return Ok(session);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ErrorResponse { Code = 400, Success = false, Message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResponse { Code = 400, Success = false, Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording public parking entry");
            return StatusCode(500, new ErrorResponse { Code = 500, Success = false, Message = "An error occurred while recording entry." });
        }
    }

    /// <summary>
    /// Look up an active parking ticket by vehicle plate number or session code.
    /// </summary>
    [HttpGet("sessions/lookup")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PublicParkingSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PublicParkingSessionDto>> LookupSession(
        [FromQuery] string query,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest(new ErrorResponse { Code = 400, Success = false, Message = "License plate or ticket code is required." });
        }

        var session = await _publicParkingService.LookupActiveSessionAsync(query, ct);
        if (session == null)
        {
            return NotFound(new ErrorResponse { Code = 404, Success = false, Message = "No active parking session found for this license plate or ticket." });
        }

        return Ok(session);
    }

    /// <summary>
    /// Pay parking fee via ParkJom Wallet (Authenticated Commuter).
    /// </summary>
    [HttpPost("sessions/{sessionCode}/checkout-wallet")]
    [Authorize]
    [ProducesResponseType(typeof(PublicParkingSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PublicParkingSessionDto>> CheckoutWallet(
        string sessionCode,
        CancellationToken ct)
    {
        var user = await _currentUser.GetCurrentUserAsync();
        if (user == null)
        {
            return StatusCode(403, new ErrorResponse { Code = 403, Success = false, Message = "User authentication required." });
        }

        try
        {
            var result = await _publicParkingService.PayWithWalletAsync(user.UserId, sessionCode, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResponse { Code = 400, Success = false, Message = ex.Message });
        }
    }

    /// <summary>
    /// Create Stripe Checkout Session for parking payment (Supports guest checkout without login).
    /// </summary>
    [HttpPost("sessions/{sessionCode}/checkout-stripe")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PublicParkingCheckoutStripeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PublicParkingCheckoutStripeResponse>> CheckoutStripe(
        string sessionCode,
        [FromQuery] string returnTarget = "web",
        CancellationToken ct = default)
    {
        try
        {
            var user = await _currentUser.GetCurrentUserAsync();
            var response = await _publicParkingService.CreateStripeCheckoutAsync(sessionCode, returnTarget, user?.UserId, ct);
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResponse { Code = 400, Success = false, Message = ex.Message });
        }
    }

    /// <summary>
    /// Stripe payment callback bridge.
    /// </summary>
    [HttpGet("stripe/success")]
    [AllowAnonymous]
    public async Task<IActionResult> StripeSuccess(
        [FromQuery] string session_id,
        [FromQuery] string sessionCode,
        [FromQuery] string returnTarget = "web",
        CancellationToken ct = default)
    {
        await _publicParkingService.ProcessStripeCompletedAsync(session_id, null, ct);

        var webUrl = _configuration["AppUrls:WebReturnUrl"] ?? "https://united-perigee-400000.web.app/commuter";
        var redirectUrl = $"{webUrl}?public_paid=true&sessionCode={sessionCode}";
        return Redirect(redirectUrl);
    }

    /// <summary>
    /// Exit barrier scan. Validates payment status; spot is released if paid or within initial grace period.
    /// </summary>
    [HttpPost("exit")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PublicParkingSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PublicParkingSessionDto>> RecordExit(
        [FromBody] PublicParkingExitRequest request,
        CancellationToken ct)
    {
        try
        {
            var session = await _publicParkingService.RecordExitAsync(request, ct);
            return Ok(session);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResponse { Code = 400, Success = false, Message = ex.Message });
        }
    }

    /// <summary>
    /// Owner: Register a new public parking area with capacity, operating hours and hourly rates.
    /// </summary>
    [HttpPost("/api/owner/public-parking/lots")]
    [Authorize]
    [ProducesResponseType(typeof(PublicParkingLotDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PublicParkingLotDto>> CreatePublicLot(
        [FromBody] CreatePublicParkingLotRequest request,
        CancellationToken ct)
    {
        var owner = await _currentUser.GetCurrentUserAsync();
        if (owner == null || owner.UserType != UserType.PropertyOwner)
        {
            return StatusCode(403, new ErrorResponse { Code = 403, Success = false, Message = "Only property owners can register public parking areas." });
        }

        var lot = await _publicParkingService.CreateLotAsync(owner.UserId, request, ct);
        return Ok(lot);
    }

    /// <summary>
    /// Owner: Update public parking lot settings.
    /// </summary>
    [HttpPut("/api/owner/public-parking/lots/{id:int}")]
    [Authorize]
    [ProducesResponseType(typeof(PublicParkingLotDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PublicParkingLotDto>> UpdatePublicLot(
        int id,
        [FromBody] UpdatePublicParkingLotRequest request,
        CancellationToken ct)
    {
        var owner = await _currentUser.GetCurrentUserAsync();
        if (owner == null || owner.UserType != UserType.PropertyOwner)
        {
            return StatusCode(403, new ErrorResponse { Code = 403, Success = false, Message = "Only property owners can update public parking areas." });
        }

        var lot = await _publicParkingService.UpdateLotAsync(owner.UserId, id, request, ct);
        if (lot == null) return NotFound(new ErrorResponse { Code = 404, Success = false, Message = "Lot not found or unauthorized." });
        return Ok(lot);
    }

    /// <summary>
    /// Owner: List live active parked vehicles for a lot.
    /// </summary>
    [HttpGet("/api/owner/public-parking/lots/{id:int}/sessions")]
    [Authorize]
    [ProducesResponseType(typeof(List<PublicParkingSessionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<PublicParkingSessionDto>>> GetLotActiveSessions(
        int id,
        CancellationToken ct)
    {
        var owner = await _currentUser.GetCurrentUserAsync();
        if (owner == null) return Unauthorized();

        var sessions = await _publicParkingService.GetActiveSessionsForOwnerAsync(owner.UserId, id, ct);
        return Ok(sessions);
    }
}
