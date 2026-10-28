using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkJomV2.DTOs;
using ParkJomV2.Services;
using ParkJomV2.Services.Support;

namespace ParkJomV2.Controllers.Support;

[ApiController]
[Route("api/support/context")]
[Authorize]
public class SupportContextController : ControllerBase
{
    private readonly SupportContextService _contextService;
    private readonly CurrentUserService _currentUserService;

    public SupportContextController(SupportContextService contextService, CurrentUserService currentUserService)
    {
        _contextService = contextService;
        _currentUserService = currentUserService;
    }

    /// <summary>
    /// U-01: Get active support context (booking, spot, vehicle, IoT status, access log summary).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(SupportApiResponse<SupportContextDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<SupportApiResponse<SupportContextDto>>> GetContext(
        [FromQuery] int? bookingId = null,
        [FromQuery] int? vehicleId = null)
    {
        var userId = _currentUserService.UserId;
        if (!userId.HasValue) return Unauthorized();

        var context = await _contextService.GetUserContextAsync(userId.Value, bookingId, vehicleId);
        return Ok(new SupportApiResponse<SupportContextDto>
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Support context retrieved successfully",
            Data = context
        });
    }
}
