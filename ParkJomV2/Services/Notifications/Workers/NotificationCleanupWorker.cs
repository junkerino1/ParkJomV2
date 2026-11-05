using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;

namespace ParkJomV2.Services.Notifications.Workers;

public class NotificationCleanupWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<NotificationCleanupWorker> _logger;

    public NotificationCleanupWorker(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<NotificationCleanupWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("NotificationCleanupWorker initialized.");

        // Initial delay so app startup is not blocked or slowed down
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in NotificationCleanupWorker.");
            }

            // Run once every 24 hours
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }

        _logger.LogInformation("NotificationCleanupWorker stopped.");
    }

    private async Task RunCleanupAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

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
            _logger.LogInformation("NotificationCleanupWorker removed {Count} dead push subscriptions.", deadSubscriptions.Count);
        }
    }
}
