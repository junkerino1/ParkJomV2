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
    private readonly CloudinaryService _cloudinaryService;
    private readonly ILogger<PublicParkingLotController> _logger;

    public PublicParkingLotController(
        PublicParkingService publicParkingService,
        CurrentUserService currentUser,
        IConfiguration configuration,
        CloudinaryService cloudinaryService,
        ILogger<PublicParkingLotController> logger)
    {
        _publicParkingService = publicParkingService;
        _currentUser = currentUser;
        _configuration = configuration;
        _cloudinaryService = cloudinaryService;
        _logger = logger;
    }

    /// <summary>
    /// List all public parking lots with real-time remaining bays and opening status.
    /// Pass all=true to include pending/rejected lots (e.g. for administration).
    /// </summary>
    [HttpGet("lots")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(List<PublicParkingLotDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<PublicParkingLotDto>>> GetLots([FromQuery] bool all = false, CancellationToken ct = default)
    {
        var lots = await _publicParkingService.GetLotsAsync(all, ct);
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
    /// Owner: List all public parking areas registered by this owner with verification and IoT status.
    /// </summary>
    [HttpGet("/api/owner/public-parking/lots")]
    [Authorize]
    [ProducesResponseType(typeof(List<PublicParkingLotDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<PublicParkingLotDto>>> GetOwnerLots(CancellationToken ct)
    {
        var owner = await _currentUser.GetCurrentUserAsync();
        if (owner == null) return Unauthorized();

        var lots = await _publicParkingService.GetLotsForOwnerAsync(owner.UserId, ct);
        return Ok(lots);
    }

    /// <summary>
    /// Admin / Owner review: Set verification status (Approve/Reject) and implement/bind IoT barrier device.
    /// </summary>
    [HttpPost("/api/public-parking/lots/{id:int}/review")]
    [Authorize]
    [ProducesResponseType(typeof(PublicParkingLotDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PublicParkingLotDto>> ReviewLot(
        int id,
        [FromBody] ReviewPublicParkingLotRequest request,
        CancellationToken ct)
    {
        var reviewer = await _currentUser.GetCurrentUserAsync();
        if (reviewer == null) return Unauthorized();

        var lot = await _publicParkingService.ReviewLotAsync(id, request, ct);
        if (lot == null) return NotFound(new ErrorResponse { Code = 404, Success = false, Message = "Lot not found." });
        return Ok(lot);
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
    /// Owner or Admin: Update public parking lot settings (pricing, operating hours, capacity, metadata).
    /// </summary>
    [HttpPut("/api/owner/public-parking/lots/{id:int}")]
    [HttpPut("/api/admin/public-parking/lots/{id:int}")]
    [Authorize]
    [ProducesResponseType(typeof(PublicParkingLotDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PublicParkingLotDto>> UpdatePublicLot(
        int id,
        [FromBody] UpdatePublicParkingLotRequest request,
        CancellationToken ct)
    {
        var user = await _currentUser.GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var isAdmin = user.UserType == UserType.Admin;
        var isOwner = user.UserType == UserType.PropertyOwner;
        if (!isAdmin && !isOwner)
        {
            return StatusCode(403, new ErrorResponse { Code = 403, Success = false, Message = "Only property owners and admins can update public parking areas." });
        }

        var lot = await _publicParkingService.UpdateLotAsync(isAdmin ? null : user.UserId, id, request, ct);
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

    /// <summary>
    /// Upload public parking photo (facility or entrance) to Cloudinary.
    /// </summary>
    [HttpPost("/api/owner/public-parking/upload-photo")]
    [Authorize]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(15 * 1024 * 1024)]
    [ProducesResponseType(typeof(PublicParkingUploadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PublicParkingUploadResponse>> UploadPhoto([FromForm] PublicParkingFileUploadRequest request, CancellationToken ct)
    {
        var file = request?.File;
        if (file == null || file.Length == 0)
        {
            return BadRequest(new ErrorResponse { Code = 400, Success = false, Message = "Photo file is required." });
        }

        var result = await _cloudinaryService.UploadImageAsync(file, "parkjom/public-parking");
        return Ok(new PublicParkingUploadResponse { Url = result.SecureUrl.ToString() });
    }

    /// <summary>
    /// Upload public parking operator verification document to Cloudinary.
    /// </summary>
    [HttpPost("/api/owner/public-parking/upload-document")]
    [Authorize]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(25 * 1024 * 1024)]
    [ProducesResponseType(typeof(PublicParkingUploadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PublicParkingUploadResponse>> UploadDocument([FromForm] PublicParkingFileUploadRequest request, CancellationToken ct)
    {
        var file = request?.File;
        if (file == null || file.Length == 0)
        {
            return BadRequest(new ErrorResponse { Code = 400, Success = false, Message = "Document file is required." });
        }

        var result = await _cloudinaryService.UploadPrivateDocumentAsync(file, "parkjom/public-parking/documents");
        return Ok(new PublicParkingUploadResponse { Url = result.SecureUrl.ToString() });
    }

    /// <summary>
    /// Admin or Owner: Delete a public parking lot.
    /// If user is Admin, they can delete any lot.
    /// If user is Property Owner, they can only delete lots they own.
    /// </summary>
    [HttpDelete("lots/{id:int}")]
    [HttpDelete("/api/owner/public-parking/lots/{id:int}")]
    [HttpDelete("/api/admin/public-parking/lots/{id:int}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeletePublicLot(int id, CancellationToken ct)
    {
        var user = await _currentUser.GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var isAdmin = user.UserType == UserType.Admin;
        int? ownerFilter = isAdmin ? null : user.UserId;

        var success = await _publicParkingService.DeleteLotAsync(id, ownerFilter, ct);
        if (!success)
        {
            return NotFound(new ErrorResponse
            {
                Code = 404,
                Success = false,
                Message = isAdmin ? "Public parking lot not found." : "Public parking lot not found or you are not authorized to delete it."
            });
        }

        return Ok(new { success = true, message = "Public parking lot deleted successfully." });
    }
}
