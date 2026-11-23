using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkJomV2.DTOs;
using ParkJomV2.Services;
using ParkJomV2.Services.Settlement;

namespace ParkJomV2.Controllers;

[ApiController]
[Route("api/admin/settlement")]
[Authorize(Policy = "AdminOnly")]
public sealed class AdminSettlementController : ControllerBase
{
    private readonly SettlementService _settlementService;
    private readonly CurrentUserService _currentUser;
    private readonly ILogger<AdminSettlementController> _logger;

    public AdminSettlementController(
        SettlementService settlementService,
        CurrentUserService currentUser,
        ILogger<AdminSettlementController> logger)
    {
        _settlementService = settlementService;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet("summary")]
    [ProducesResponseType(typeof(AdminSettlementSummaryResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminSettlementSummaryResponse>> GetSummary(CancellationToken ct)
    {
        var summary = await _settlementService.GetSummaryAsync(ct);
        return Ok(new AdminSettlementSummaryResponse
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Settlement summary retrieved successfully.",
            Data = summary
        });
    }

    [HttpGet("withdrawals")]
    [ProducesResponseType(typeof(AdminWithdrawalListResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminWithdrawalListResponse>> GetWithdrawals(
        [FromQuery] string? status = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _settlementService.GetWithdrawalsAsync(status, search, page, pageSize, ct);
        return Ok(result);
    }

    [HttpPost("withdrawals/{transactionId:int}/decision")]
    [ProducesResponseType(typeof(WithdrawalDecisionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WithdrawalDecisionResponse>> DecideWithdrawal(
        int transactionId,
        [FromBody] WithdrawalDecisionRequest request,
        CancellationToken ct)
    {
        try
        {
            var updated = await _settlementService.ProcessWithdrawalDecisionAsync(
                transactionId,
                request,
                _currentUser.UserId,
                ct);

            return Ok(new WithdrawalDecisionResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = request.Approve
                    ? $"Withdrawal {updated.ReferenceNumber} cleared and settled."
                    : $"Withdrawal {updated.ReferenceNumber} rejected and refunded to owner wallet.",
                Data = updated
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ErrorResponse { Code = StatusCodes.Status404NotFound, Success = false, Message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResponse { Code = StatusCodes.Status400BadRequest, Success = false, Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decide withdrawal {TransactionId}", transactionId);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while updating the withdrawal request."
            });
        }
    }

    [HttpGet("batches")]
    [ProducesResponseType(typeof(SettlementBatchListResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<SettlementBatchListResponse>> GetBatches(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _settlementService.GetBatchesAsync(page, pageSize, ct);
        return Ok(result);
    }

    [HttpGet("batches/{batchId:int}")]
    [ProducesResponseType(typeof(SettlementBatchDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SettlementBatchDetailResponse>> GetBatchDetail(int batchId, CancellationToken ct)
    {
        var detail = await _settlementService.GetBatchDetailAsync(batchId, ct);
        if (detail is null)
        {
            return NotFound(new ErrorResponse
            {
                Code = StatusCodes.Status404NotFound,
                Success = false,
                Message = $"Settlement batch #{batchId} was not found."
            });
        }

        return Ok(new SettlementBatchDetailResponse
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Settlement batch retrieved successfully.",
            Data = detail
        });
    }

    [HttpPost("batches/generate")]
    [ProducesResponseType(typeof(SettlementBatchDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SettlementBatchDetailResponse>> GenerateBatch(
        [FromBody] GenerateSettlementBatchRequest? request,
        CancellationToken ct)
    {
        try
        {
            var detail = await _settlementService.GenerateBatchAsync(
                request ?? new GenerateSettlementBatchRequest(),
                _currentUser.UserId,
                ct);

            return Ok(new SettlementBatchDetailResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = $"Batch {detail.Batch.BatchReference} generated ({detail.Batch.ItemCount} items, RM {detail.Batch.TotalAmount:F2}).",
                Data = detail
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResponse { Code = StatusCodes.Status400BadRequest, Success = false, Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate settlement batch");
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while generating the settlement batch."
            });
        }
    }

    [HttpPost("batches/{batchId:int}/submit")]
    [ProducesResponseType(typeof(SettlementBatchDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SettlementBatchDetailResponse>> SubmitBatch(int batchId, CancellationToken ct)
    {
        try
        {
            var detail = await _settlementService.SubmitBatchAsync(batchId, _currentUser.UserId, ct);
            return Ok(new SettlementBatchDetailResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = $"Batch {detail.Batch.BatchReference} marked as submitted to bank.",
                Data = detail
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ErrorResponse { Code = StatusCodes.Status404NotFound, Success = false, Message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResponse { Code = StatusCodes.Status400BadRequest, Success = false, Message = ex.Message });
        }
    }

    [HttpPost("batches/{batchId:int}/clear")]
    [ProducesResponseType(typeof(SettlementBatchDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SettlementBatchDetailResponse>> ClearBatch(
        int batchId,
        [FromBody] AdminBatchClearingRequest? request,
        CancellationToken ct)
    {
        try
        {
            var detail = await _settlementService.ApplyBankClearingUpdateAsync(
                batchId,
                null,
                request?.BankReference,
                request?.Items,
                _currentUser.UserId,
                "AdminClearingSync",
                ct);

            return Ok(new SettlementBatchDetailResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = $"Batch {detail.Batch.BatchReference} clearing synchronized ({detail.Batch.Status}).",
                Data = detail
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ErrorResponse { Code = StatusCodes.Status404NotFound, Success = false, Message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResponse { Code = StatusCodes.Status400BadRequest, Success = false, Message = ex.Message });
        }
    }

    [HttpGet("batches/{batchId:int}/ibg-file")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadIbgFile(int batchId, CancellationToken ct)
    {
        try
        {
            var (bytes, fileName) = await _settlementService.BuildIbgFileAsync(batchId, ct);
            return File(bytes, "text/plain; charset=utf-8", fileName);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ErrorResponse { Code = StatusCodes.Status404NotFound, Success = false, Message = ex.Message });
        }
    }

    [HttpGet("ledger")]
    [ProducesResponseType(typeof(SettlementLedgerResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<SettlementLedgerResponse>> GetLedger(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string? type = null,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var result = await _settlementService.GetLedgerAsync(from, to, type, status, search, page, pageSize, ct);
        return Ok(result);
    }

    [HttpGet("export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportLedger(
        [FromQuery] string format = "csv",
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] string? type = null,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null,
        CancellationToken ct = default)
    {
        var (content, contentType, fileName) = await _settlementService.BuildExportAsync(
            format,
            from,
            to,
            type,
            status,
            search,
            _currentUser.UserId,
            ct);

        return File(content, contentType, fileName);
    }

    [HttpPost("escrow/sync")]
    [ProducesResponseType(typeof(EscrowSyncResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<EscrowSyncResponse>> SyncEscrow(CancellationToken ct)
    {
        var result = await _settlementService.SyncEscrowLedgerAsync(_currentUser.UserId, ct);
        return Ok(new EscrowSyncResponse
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = result.ReleasedBookingsCount > 0
                ? $"Escrow sync released {result.ReleasedBookingsCount} booking(s) totaling RM {result.ReleasedAmount:F2}."
                : "Escrow ledger is already synchronized.",
            Data = result
        });
    }

    [HttpGet("reconciliation")]
    [ProducesResponseType(typeof(ReconciliationReportResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ReconciliationReportResponse>> GetReconciliation(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        CancellationToken ct = default)
    {
        var report = await _settlementService.RunReconciliationAsync(from, to, _currentUser.UserId, ct);
        return Ok(new ReconciliationReportResponse
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Financial reconciliation completed.",
            Data = report
        });
    }
}
