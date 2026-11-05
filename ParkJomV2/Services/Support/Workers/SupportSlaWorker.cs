using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.Services.Support.Workers;

public class SupportSlaJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SupportSlaJob> _logger;

    public SupportSlaJob(IServiceScopeFactory scopeFactory, ILogger<SupportSlaJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var realtimeNotifier = scope.ServiceProvider.GetRequiredService<ISupportRealtimeNotifier>();
        var now = DateTime.UtcNow;
        var breachedTickets = await context.SupportTickets
            .Where(t => (t.Status == SupportTicketStatus.New || t.Status == SupportTicketStatus.Assigned || t.Status == SupportTicketStatus.InProgress)
                && ((t.FirstResponseDueAt.HasValue && t.FirstResponseDueAt.Value < now && !t.FirstResponseAt.HasValue)
                    || (t.ResolutionDueAt.HasValue && t.ResolutionDueAt.Value < now && !t.ResolvedAt.HasValue)))
            .OrderBy(t => t.TicketId)
            .Take(50)
            .ToListAsync(cancellationToken);

        if (breachedTickets.Count > 0)
        {
            _logger.LogWarning("Found {Count} tickets currently breaching SLA response/resolution deadlines", breachedTickets.Count);
            await realtimeNotifier.BroadcastEventAsync("sla.breach_warning", new { count = breachedTickets.Count, timestamp = now });
        }
        return breachedTickets.Count;
    }
}
