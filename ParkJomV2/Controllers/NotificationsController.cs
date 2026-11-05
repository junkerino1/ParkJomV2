using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkJomV2.DTOs;
using ParkJomV2.Services;
using ParkJomV2.Services.Notifications;

namespace ParkJomV2.Controllers;

[ApiController]
[Authorize]
[Route("api/notifications")]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _notificationService;
    private readonly CurrentUserService _currentUser;
    private readonly IConfiguration _configuration;
    private readonly ILogger<NotificationsController> _logger;

    public NotificationsController(
        INotificationService notificationService,
        CurrentUserService currentUser,
        IConfiguration configuration,
        ILogger<NotificationsController> logger)
    {
        _notificationService = notificationService;
        _currentUser = currentUser;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>Returns cursor-paginated inbox notifications for the authenticated user.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(NotificationInboxResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<NotificationInboxResponse>> GetInbox([FromQuery] NotificationQuery query)
    {
        var userId = _currentUser.UserId;
        if (!userId.HasValue)
        {
            return Unauthorized(new ErrorResponse { Code = 401, Success = false, Message = "Authentication required." });
        }

        var result = await _notificationService.GetInboxAsync(userId.Value, query);
        return Ok(result);
    }

    /// <summary>Returns the total and category breakdown of unread notifications for badge rendering.</summary>
    [HttpGet("unread-count")]
    [ProducesResponseType(typeof(UnreadCountDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<UnreadCountDto>> GetUnreadCount()
    {
        var userId = _currentUser.UserId;
        if (!userId.HasValue)
        {
            return Unauthorized(new ErrorResponse { Code = 401, Success = false, Message = "Authentication required." });
        }

        var count = await _notificationService.GetUnreadCountAsync(userId.Value);
        return Ok(count);
    }

    /// <summary>Marks specific notification IDs as read.</summary>
    [HttpPost("read")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkRead([FromBody] MarkReadRequestDto request)
    {
        var userId = _currentUser.UserId;
        if (!userId.HasValue)
        {
            return Unauthorized(new ErrorResponse { Code = 401, Success = false, Message = "Authentication required." });
        }

        if (request.Ids == null || request.Ids.Count == 0)
        {
            return Ok(new { success = true, markedCount = 0 });
        }

        var count = await _notificationService.MarkReadAsync(userId.Value, request.Ids);
        return Ok(new { success = true, markedCount = count });
    }

    /// <summary>Marks all notifications as read (optionally filtered by category).</summary>
    [HttpPost("read-all")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> MarkAllRead([FromQuery] string? category = null)
    {
        var userId = _currentUser.UserId;
        if (!userId.HasValue)
        {
            return Unauthorized(new ErrorResponse { Code = 401, Success = false, Message = "Authentication required." });
        }

        var count = await _notificationService.MarkAllReadAsync(userId.Value, category);
        return Ok(new { success = true, markedCount = count });
    }

    /// <summary>Soft-deletes/archives a notification by id.</summary>
    [HttpDelete("{id:long}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Archive(long id)
    {
        var userId = _currentUser.UserId;
        if (!userId.HasValue)
        {
            return Unauthorized(new ErrorResponse { Code = 401, Success = false, Message = "Authentication required." });
        }

        var archived = await _notificationService.ArchiveAsync(userId.Value, id);
        if (!archived)
        {
            return NotFound(new ErrorResponse { Code = 404, Success = false, Message = "Notification not found." });
        }

        return Ok(new { success = true, message = "Notification archived." });
    }

    /// <summary>Gets the user's notification preferences matrix.</summary>
    [HttpGet("preferences")]
    [ProducesResponseType(typeof(NotificationPreferencesResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<NotificationPreferencesResponse>> GetPreferences()
    {
        var userId = _currentUser.UserId;
        if (!userId.HasValue)
        {
            return Unauthorized(new ErrorResponse { Code = 401, Success = false, Message = "Authentication required." });
        }

        var prefs = await _notificationService.GetPreferencesAsync(userId.Value);
        return Ok(prefs);
    }

    /// <summary>Updates notification preferences.</summary>
    [HttpPut("preferences")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdatePreferences([FromBody] UpdateNotificationPreferencesRequest request)
    {
        var userId = _currentUser.UserId;
        if (!userId.HasValue)
        {
            return Unauthorized(new ErrorResponse { Code = 401, Success = false, Message = "Authentication required." });
        }

        await _notificationService.UpdatePreferencesAsync(userId.Value, request);
        return Ok(new { success = true, message = "Notification preferences updated." });
    }

    /// <summary>Returns the VAPID public key needed by the browser to create a PushSubscription.</summary>
    [HttpGet("push/vapid-public-key")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetVapidPublicKey()
    {
        var publicKey = _configuration["Notifications:WebPush:PublicKey"] ?? "";
        return Ok(new { publicKey });
    }

    /// <summary>Registers a browser Web Push subscription for this user.</summary>
    [HttpPost("push/subscriptions")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> RegisterPushSubscription([FromBody] PushSubscriptionRequestDto request)
    {
        var userId = _currentUser.UserId;
        if (!userId.HasValue)
        {
            return Unauthorized(new ErrorResponse { Code = 401, Success = false, Message = "Authentication required." });
        }

        await _notificationService.RegisterPushSubscriptionAsync(userId.Value, request);
        return Ok(new { success = true, message = "Push subscription registered." });
    }

    /// <summary>Unregisters a browser Web Push subscription (e.g. on logout).</summary>
    [HttpDelete("push/subscriptions")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> UnregisterPushSubscription([FromQuery] string endpoint)
    {
        var userId = _currentUser.UserId;
        if (!userId.HasValue)
        {
            return Unauthorized(new ErrorResponse { Code = 401, Success = false, Message = "Authentication required." });
        }

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return BadRequest(new ErrorResponse { Code = 400, Success = false, Message = "Endpoint is required." });
        }

        await _notificationService.UnregisterPushSubscriptionAsync(userId.Value, endpoint);
        return Ok(new { success = true, message = "Push subscription removed." });
    }
}
