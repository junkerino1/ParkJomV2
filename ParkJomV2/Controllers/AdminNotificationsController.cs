using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services.Notifications;

namespace ParkJomV2.Controllers;

[ApiController]
[Authorize(Policy = "AdminOnly")]
[Route("api/admin/notifications")]
public class AdminNotificationsController : ControllerBase
{
    private readonly INotificationService _notificationService;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<AdminNotificationsController> _logger;

    public AdminNotificationsController(
        INotificationService notificationService,
        ApplicationDbContext context,
        ILogger<AdminNotificationsController> logger)
    {
        _notificationService = notificationService;
        _context = context;
        _logger = logger;
    }

    /// <summary>Dispatches a system broadcast notification to all active users or a specific user role.</summary>
    [HttpPost("broadcast")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Broadcast([FromBody] AdminBroadcastRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Body))
        {
            return BadRequest(new ErrorResponse { Code = 400, Success = false, Message = "Title and Body are required." });
        }

        var priority = Enum.TryParse<NotificationPriority>(request.Priority, true, out var p)
            ? p
            : NotificationPriority.Normal;

        var data = new Dictionary<string, object?>
        {
            ["title"] = request.Title.Trim(),
            ["body"] = request.Body.Trim(),
            ["actionUrl"] = request.ActionUrl?.Trim()
        };

        var publishReq = new NotificationPublishRequest(
            userId: 0,
            type: !string.IsNullOrWhiteSpace(request.Type) ? request.Type.Trim() : "system.announcement",
            data: data,
            actionUrl: request.ActionUrl?.Trim(),
            priorityOverride: priority
        );

        int count;
        if (!string.IsNullOrWhiteSpace(request.TargetRole) &&
            Enum.TryParse<UserType>(request.TargetRole, true, out var role))
        {
            count = await _notificationService.PublishToRoleAsync(role, publishReq);
        }
        else
        {
            count = await _notificationService.PublishToAllAsync(publishReq);
        }

        _logger.LogInformation("Admin broadcast notification '{Title}' dispatched to {Count} users.", request.Title, count);

        return Ok(new
        {
            success = true,
            recipientCount = count,
            message = $"Broadcast dispatched to {count} users."
        });
    }

    /// <summary>Gets delivery audit logs (optionally filtered by status, e.g. Failed).</summary>
    [HttpGet("deliveries")]
    [ProducesResponseType(typeof(List<DeliveryStatusDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<DeliveryStatusDto>>> GetDeliveries(
        [FromQuery] string? status = null,
        [FromQuery] int limit = 50)
    {
        limit = Math.Clamp(limit, 1, 100);

        var query = _context.NotificationDeliveries
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<DeliveryStatus>(status, true, out var s))
        {
            query = query.Where(d => d.Status == s);
        }

        var deliveries = await query
            .OrderByDescending(d => d.DeliveryId)
            .Take(limit)
            .Select(d => new DeliveryStatusDto
            {
                DeliveryId = d.DeliveryId,
                NotificationId = d.NotificationId,
                Channel = d.Channel.ToString(),
                Status = d.Status.ToString(),
                AttemptCount = d.AttemptCount,
                NextAttemptAt = d.NextAttemptAt,
                LastError = d.LastError,
                SentAt = d.SentAt,
                CreatedAt = d.CreatedAt
            })
            .ToListAsync();

        return Ok(deliveries);
    }
}
