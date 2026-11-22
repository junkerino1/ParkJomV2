using ParkJomV2.DTOs;

namespace ParkJomV2.Services.Settlement;

/// <summary>Scheduled twice-weekly IBG batch generation job invoked by the VM cron endpoint.</summary>
public sealed class SettlementBatchJob
{
    private readonly SettlementService _settlementService;
    private readonly ILogger<SettlementBatchJob> _logger;

    public SettlementBatchJob(SettlementService settlementService, ILogger<SettlementBatchJob> logger)
    {
        _settlementService = settlementService;
        _logger = logger;
    }

    public async Task<int> SyncEscrowOnlyAsync(CancellationToken cancellationToken)
    {
        var syncResult = await _settlementService.SyncEscrowLedgerAsync(adminUserId: null, cancellationToken);
        _logger.LogInformation(
            "Scheduled escrow sync released {Count} booking(s) totaling RM {Amount:F2}.",
            syncResult.ReleasedBookingsCount,
            syncResult.ReleasedAmount);
        return syncResult.ReleasedBookingsCount;
    }

    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        await _settlementService.SyncEscrowLedgerAsync(adminUserId: null, cancellationToken);

        try
        {
            var detail = await _settlementService.GenerateBatchAsync(
                new GenerateSettlementBatchRequest
                {
                    Rail = "IBG",
                    IncludeAutoWithdrawOwners = true,
                    ScheduleWindow = "Twice Weekly Automated Run (Tue & Fri 14:00 MYT)",
                    Notes = "Automated scheduled BNM IBG batch generation."
                },
                adminUserId: null,
                cancellationToken);

            _logger.LogInformation(
                "Scheduled IBG batch {BatchRef} generated with {Count} items (RM {Total:F2}).",
                detail.Batch.BatchReference,
                detail.Batch.ItemCount,
                detail.Batch.TotalAmount);
            return detail.Batch.ItemCount;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogInformation("Scheduled IBG batch skipped: {Message}", ex.Message);
            return 0;
        }
    }
}
