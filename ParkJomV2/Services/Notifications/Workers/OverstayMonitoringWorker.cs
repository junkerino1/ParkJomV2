using ParkJomV2.Services.Overstay;

namespace ParkJomV2.Services.Notifications.Workers;

/// <summary>Delegates minute-by-minute overstay scanning to the unified OverstayEnforcementService.</summary>
public sealed class OverstayMonitoringJob
{
    private readonly IServiceScopeFactory _scopeFactory;

    public OverstayMonitoringJob(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var enforcement = scope.ServiceProvider.GetRequiredService<OverstayEnforcementService>();
        var result = await enforcement.RunScanAsync(triggeredByAdminId: null, cancellationToken);
        return result.WarningsDispatched + result.PenaltiesDebited;
    }
}
