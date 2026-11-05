using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;

namespace ParkJomV2.Services.Notifications.Workers;

public class NotificationCleanupJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<NotificationCleanupJob> _logger;

    public NotificationCleanupJob(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<NotificationCleanupJob> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var removedCount = 0;

        var retentionDays = _configuration.GetValue<int?>("Notifications:RetentionDays") ?? 90;
        var now = DateTime.UtcNow;
        var cutoff = now.AddDays(-retentionDays);

        // 1. Purge expired notifications or read/archived notifications past retention
        var staleNotifications = await context.Notifications
            .Where(n => (n.ExpiresAt != null && n.ExpiresAt < now) ||
                        ((n.ArchivedAt != null || n.ReadAt != null) && n.CreatedAt < cutoff))
            .Take(500)
            .ToListAsync(ct);

        if (staleNotifications.Count > 0)
        {
            context.Notifications.RemoveRange(staleNotifications);
            await context.SaveChangesAsync(ct);
            removedCount += staleNotifications.Count;
            _logger.LogInformation("NotificationCleanupWorker purged {Count} stale/expired notifications.", staleNotifications.Count);
        }

        // 2. Purge push subscriptions unused for over 180 days
        var subCutoff = now.AddDays(-180);
        var deadSubscriptions = await context.PushSubscriptions
            .Where(s => (s.LastUsedAt != null && s.LastUsedAt < subCutoff) ||
                        (s.LastUsedAt == null && s.CreatedAt < subCutoff))
            .Take(100)
            .ToListAsync(ct);

        if (deadSubscriptions.Count > 0)
        {
            context.PushSubscriptions.RemoveRange(deadSubscriptions);
            await context.SaveChangesAsync(ct);
            removedCount += deadSubscriptions.Count;
            _logger.LogInformation("NotificationCleanupWorker removed {Count} dead push subscriptions.", deadSubscriptions.Count);
        }

        return removedCount;
    }
}
