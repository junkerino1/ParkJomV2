using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkJomV2.DTOs;
using ParkJomV2.Services;
using ParkJomV2.Services.Overstay;

namespace ParkJomV2.Controllers;

[ApiController]
[Route("api/admin/overstays")]
[Authorize(Policy = "AdminOnly")]
public sealed class AdminOverstayController : ControllerBase
{
    private readonly OverstayEnforcementService _overstayService;
    private readonly CurrentUserService _currentUser;
    private readonly ILogger<AdminOverstayController> _logger;

    public AdminOverstayController(
        OverstayEnforcementService overstayService,
        CurrentUserService currentUser,
        ILogger<AdminOverstayController> logger)
    {
        _overstayService = overstayService;
        _currentUser = currentUser;
        _logger = logger;
    }

    [HttpGet("summary")]
    [ProducesResponseType(typeof(OverstaySummaryResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OverstaySummaryResponse>> GetSummary(CancellationToken ct)
    {
        var summary = await _overstayService.GetSummaryAsync(ct);
        return Ok(new OverstaySummaryResponse
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Overstay enforcement summary retrieved.",
            Data = summary
        });
    }

    [HttpGet]
    [ProducesResponseType(typeof(OverstayCaseListResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OverstayCaseListResponse>> GetCases(
        [FromQuery] string? status = null,
        [FromQuery] string? paymentStatus = null,
        [FromQuery] bool? hardwareLocked = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _overstayService.GetCasesAsync(status, paymentStatus, hardwareLocked, search, page, pageSize, ct);
        return Ok(result);
    }

    [HttpPost("scan")]
    [ProducesResponseType(typeof(OverstayScanResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OverstayScanResponse>> TriggerScan(CancellationToken ct)
    {
        try
        {
            var result = await _overstayService.RunScanAsync(_currentUser.UserId, ct);
            return Ok(new OverstayScanResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = $"Scan complete: {result.CasesDetectedOrUpdated} case(s) updated, {result.WarningsDispatched} warning(s) sent, {result.PenaltiesDebited} ledger debit(s).",
                Data = result
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual overstay scan failed.");
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while running the overstay scan."
            });
        }
    }

    [HttpPost("{caseId:int}/warn")]
    [ProducesResponseType(typeof(OverstayCaseActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OverstayCaseActionResponse>> DispatchWarning(
        int caseId,
        [FromBody] OverstayWarnRequest? request,
        CancellationToken ct)
    {
        try
        {
            var updated = await _overstayService.SendWarningAsync(
                caseId,
                _currentUser.UserId,
                request?.ForceResend ?? true,
                ct);

            return Ok(new OverstayCaseActionResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = $"Multi-channel warning dispatched for {updated.CaseReference}.",
                Data = updated
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ErrorResponse { Code = StatusCodes.Status404NotFound, Success = false, Message = ex.Message });
        }
    }

    [HttpPost("{caseId:int}/charge")]
    [ProducesResponseType(typeof(OverstayCaseActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OverstayCaseActionResponse>> ChargePenalty(int caseId, CancellationToken ct)
    {
        try
        {
            var updated = await _overstayService.ChargePenaltyAsync(caseId, _currentUser.UserId, ct);
            return Ok(new OverstayCaseActionResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = updated.PaymentStatus == "Paid"
                    ? $"Penalty debited from renter wallet for {updated.CaseReference}."
                    : $"Insufficient renter wallet balance; {updated.CaseReference} marked PaymentDue and exit lock engaged.",
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
    }

    [HttpPost("{caseId:int}/waive")]
    [ProducesResponseType(typeof(OverstayCaseActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OverstayCaseActionResponse>> WaivePenalty(
        int caseId,
        [FromBody] OverstayWaiveRequest request,
        CancellationToken ct)
    {
        try
        {
            var updated = await _overstayService.WaivePenaltyAsync(caseId, request, _currentUser.UserId, ct);
            return Ok(new OverstayCaseActionResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = $"Penalty waiver applied to {updated.CaseReference}.",
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
    }

    [HttpPost("{caseId:int}/dispute")]
    [ProducesResponseType(typeof(OverstayCaseActionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OverstayCaseActionResponse>> ReviewDispute(
        int caseId,
        [FromBody] OverstayDisputeReviewRequest request,
        CancellationToken ct)
    {
        try
        {
            var updated = await _overstayService.ReviewDisputeAsync(caseId, request, _currentUser.UserId, ct);
            return Ok(new OverstayCaseActionResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = $"Dispute review ({request.Action}) recorded for {updated.CaseReference}.",
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
    }

    [HttpPost("{caseId:int}/release")]
    [ProducesResponseType(typeof(OverstaySecurityReleaseResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OverstaySecurityReleaseResponse>> SecurityRelease(
        int caseId,
        [FromBody] OverstaySecurityReleaseRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await _overstayService.SecurityReleaseAsync(caseId, request, _currentUser.UserId, ct);
            return Ok(new OverstaySecurityReleaseResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = result.BollardUnlockAttempted
                    ? (result.BollardUnlockSucceeded
                        ? $"Hardware lock cleared and ESP32 bollard unlocked for {result.Case.CaseReference}."
                        : $"Security release applied to {result.Case.CaseReference} (bollard command: {result.BollardOutcome}).")
                    : $"Security release applied to {result.Case.CaseReference}.",
                Data = result
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ErrorResponse { Code = StatusCodes.Status404NotFound, Success = false, Message = ex.Message });
        }
    }

    [HttpPut("policy")]
    [ProducesResponseType(typeof(SystemConfigResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<SystemConfigResponse>> UpdatePolicy(
        [FromBody] UpdateOverstayPolicyRequest request,
        CancellationToken ct)
    {
        var updated = await _overstayService.UpdatePolicyAsync(request, _currentUser.UserId, ct);
        return Ok(new SystemConfigResponse
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Overstay enforcement policy updated.",
            Data = updated
        });
    }
}
