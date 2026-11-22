using ParkJomV2.Services.Notifications.Workers;

namespace ParkJomV2.Services.Overstay;

/// <summary>Continuous hosted worker scanning active bookings for overstay expirations.</summary>
public sealed class OverstayEnforcementBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly OverstayWorkerStatusTracker _tracker;
    private readonly ILogger<OverstayEnforcementBackgroundService> _logger;

    public OverstayEnforcementBackgroundService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        OverstayWorkerStatusTracker tracker,
        ILogger<OverstayEnforcementBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _tracker = tracker;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = _configuration.GetValue<bool?>("OverstayWorker:Enabled") ?? true;
        var intervalSeconds = Math.Clamp(
            _configuration.GetValue<int?>("OverstayWorker:IntervalSeconds") ?? 60,
            15,
            3600);

        _tracker.MarkStarted(enabled, intervalSeconds);
        if (!enabled)
        {
            _logger.LogInformation("Continuous OverstayEnforcementBackgroundService is disabled by configuration.");
            return;
        }

        _logger.LogInformation("Continuous OverstayEnforcementBackgroundService started (interval {IntervalSeconds}s).", intervalSeconds);

        // Give database/host startup a brief warm-up before the first pass.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _tracker.MarkStopped();
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));
        do
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var job = scope.ServiceProvider.GetRequiredService<OverstayMonitoringJob>();
                await job.RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error inside continuous overstay enforcement loop.");
                _tracker.RecordScanFailed(ex.Message);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));

        _tracker.MarkStopped();
    }
}
