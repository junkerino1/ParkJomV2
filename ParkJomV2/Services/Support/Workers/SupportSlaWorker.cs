using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.Services.Support.Workers;

public class SupportSlaWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SupportSlaWorker> _logger;

    public SupportSlaWorker(IServiceProvider serviceProvider, ILogger<SupportSlaWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SupportSlaWorker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var realtimeNotifier = scope.ServiceProvider.GetRequiredService<ISupportRealtimeNotifier>();

                var now = DateTime.UtcNow;

                var breachedTickets = await context.SupportTickets
                    .Where(t => (t.Status == SupportTicketStatus.New || t.Status == SupportTicketStatus.Assigned || t.Status == SupportTicketStatus.InProgress)
                        && ((t.FirstResponseDueAt.HasValue && t.FirstResponseDueAt.Value < now && !t.FirstResponseAt.HasValue)
                            || (t.ResolutionDueAt.HasValue && t.ResolutionDueAt.Value < now && !t.ResolvedAt.HasValue)))
                    .OrderBy(t => t.TicketId)
                    .Take(50)
                    .ToListAsync(stoppingToken);

                if (breachedTickets.Count > 0)
                {
                    _logger.LogWarning("Found {Count} tickets currently breaching SLA response/resolution deadlines", breachedTickets.Count);
                    await realtimeNotifier.BroadcastEventAsync("sla.breach_warning", new { count = breachedTickets.Count, timestamp = now });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SupportSlaWorker");
            }

            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }
    }
}
