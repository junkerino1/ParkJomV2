using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ParkJomV2.DTOs;
using ParkJomV2.Services.Copilot;

namespace ParkJomV2.Controllers;

/// <summary>
/// AI-1: ParkJom Transit Copilot — conversational park-and-ride planner backed by Gemini function calling.
/// </summary>
[ApiController]
[Authorize]
[Route("api/copilot")]
public class CopilotController : ControllerBase
{
    public const string RateLimitPolicy = "copilot";

    private readonly CopilotService _copilot;
    private readonly ILogger<CopilotController> _logger;

    public CopilotController(CopilotService copilot, ILogger<CopilotController> logger)
    {
        _copilot = copilot;
        _logger = logger;
    }

    /// <summary>
    /// POST /api/copilot/chat — send a message (plus recent history) and receive a reply with UI cards.
    /// </summary>
    [HttpPost("chat")]
    [EnableRateLimiting(RateLimitPolicy)]
    [ProducesResponseType(typeof(CopilotChatResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<CopilotChatResponse>> Chat([FromBody] CopilotChatRequest request, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Unauthorized(new { Code = 401, Success = false, Message = "The authenticated account could not be identified." });

        try
        {
            return Ok(await _copilot.ChatAsync(request, userId, ct));
        }
        catch (GeminiException ex)
        {
            return StatusCode(ex.StatusCode, new { Code = ex.StatusCode, Success = false, Message = ex.Message });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(499);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Copilot chat failed for user {UserId}", userId);
            return StatusCode(500, new { Code = 500, Success = false, Message = "The AI assistant failed unexpectedly." });
        }
    }
}
