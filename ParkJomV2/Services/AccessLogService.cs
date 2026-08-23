using ParkJomV2.Data;
using ParkJomV2.Models;
using System.Security.Claims;

namespace ParkJomV2.Services;

/// <summary>
/// Writes system-wide action logs into the AccessLogs table.
/// Inject into a controller and call <see cref="LogAsync(ClaimsPrincipal, string, int?, int?)"/>
/// (or <see cref="LogAsync(int?, string, int?, int?)"/>) after each action.
/// BookingId / IoTDeviceId are optional — they only apply to IoT access events.
/// </summary>
public class AccessLogService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<AccessLogService> _logger;

    public AccessLogService(ApplicationDbContext context, ILogger<AccessLogService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Log the outcome of an action for the authenticated user (reads the user id from the claims).
    /// Call this AFTER the action completes — pass success=true when it succeeded, false when it failed.
    /// </summary>
    public Task LogAsync(
        ClaimsPrincipal user,
        string action,
        bool success,
        string? detail = null,
        int? bookingId = null,
        int? iotDeviceId = null)
    {
        var userIdText = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var userId = int.TryParse(userIdText, out var parsedUserId) ? parsedUserId : (int?)null;
        return LogAsync(userId, action, success, detail, bookingId, iotDeviceId);
    }

    /// <summary>Log the outcome of an action with an explicit user id (pass null for anonymous actions).</summary>
    public async Task LogAsync(
        int? userId,
        string action,
        bool success,
        string? detail = null,
        int? bookingId = null,
        int? iotDeviceId = null)
    {
        try
        {
            var outcome = success ? "Success" : "Failed";
            var message = string.IsNullOrWhiteSpace(detail)
                ? $"{action} [{outcome}]"
                : $"{action} [{outcome}] - {detail}";

            _context.AccessLogs.Add(new AccessLog
            {
                UserId = userId,
                BookingId = bookingId,
                IoTDeviceId = iotDeviceId,
                Actions = message,
                AccessedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Never let a logging failure break the actual request.
            _logger.LogError(ex, "Failed to write access log: {Action}", action);
        }
    }
}
