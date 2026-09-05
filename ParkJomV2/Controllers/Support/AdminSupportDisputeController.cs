using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkJomV2.DTOs;
using ParkJomV2.Services;
using ParkJomV2.Services.Support;

namespace ParkJomV2.Controllers.Support;

[ApiController]
[Route("api/admin/support/disputes")]
[Authorize(Policy = "AdminOnly")]
public class AdminSupportDisputeController : ControllerBase
{
    private readonly DisputeService _disputeService;
    private readonly CurrentUserService _currentUserService;

    public AdminSupportDisputeController(DisputeService disputeService, CurrentUserService currentUserService)
    {
        _disputeService = disputeService;
        _currentUserService = currentUserService;
    }

    /// <summary>
    /// A-23: Admin dispute and investigation register.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(SupportApiPagedResponse<DisputeCustomerSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<SupportApiPagedResponse<DisputeCustomerSummaryDto>>> GetDisputes(
        [FromQuery] string? status = null,
        [FromQuery] string? type = null,
        [FromQuery] string? team = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        var (items, total) = await _disputeService.GetAdminDisputesAsync(status, type, team, page, pageSize);
        return Ok(new SupportApiPagedResponse<DisputeCustomerSummaryDto>
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Dispute cases retrieved successfully",
            Data = new PagedResult<DisputeCustomerSummaryDto>
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalCount = total
            }
        });
    }

    /// <summary>
    /// A-24: Get full admin dispute details, financial ledger, evidence, and audit trail.
    /// </summary>
    [HttpGet("{disputeId:int}")]
    [ProducesResponseType(typeof(SupportApiResponse<DisputeAdminDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SupportApiResponse<DisputeAdminDto>>> GetDisputeDetail(int disputeId)
    {
        var dispute = await _disputeService.GetAdminDisputeDetailAsync(disputeId);
        if (dispute == null)
        {
            return NotFound(new SupportApiResponse<object>
            {
                Code = StatusCodes.Status404NotFound,
                Success = false,
                Message = "Dispute case not found"
            });
        }

        return Ok(new SupportApiResponse<DisputeAdminDto>
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Dispute case retrieved successfully",
            Data = dispute
        });
    }

    /// <summary>
    /// A-25: Admin uploads or registers evidence file.
    /// </summary>
    [HttpPost("{disputeId:int}/evidence")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(SupportApiResponse<DisputeEvidenceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SupportApiResponse<DisputeEvidenceDto>>> UploadEvidence(
        int disputeId,
        [FromForm] UploadDisputeEvidenceRequestDto request)
    {
        var adminId = _currentUserService.UserId;
        if (!adminId.HasValue) return Unauthorized();

        try
        {
            var evidence = await _disputeService.UploadEvidenceAsync(disputeId, adminId.Value, "Admin", request);
            return Ok(new SupportApiResponse<DisputeEvidenceDto>
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Evidence uploaded by admin",
                Data = evidence
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new SupportApiResponse<object>
            {
                Code = StatusCodes.Status404NotFound,
                Success = false,
                Message = ex.Message
            });
        }
    }

    /// <summary>
    /// A-26: Request specific evidence documents from customer.
    /// </summary>
    [HttpPost("{disputeId:int}/request-evidence")]
    [ProducesResponseType(typeof(SupportApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SupportApiResponse<object>>> RequestEvidence(
        int disputeId,
        [FromBody] RequestDisputeEvidenceRequestDto request)
    {
        var adminId = _currentUserService.UserId;
        if (!adminId.HasValue) return Unauthorized();

        try
        {
            await _disputeService.RequestEvidenceAsync(disputeId, adminId.Value, request);
            return Ok(new SupportApiResponse<object>
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Evidence request sent to customer"
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new SupportApiResponse<object>
            {
                Code = StatusCodes.Status404NotFound,
                Success = false,
                Message = ex.Message
            });
        }
    }

    /// <summary>
    /// A-27: Execute dispute investigation decision (ApproveReversal / Decline / NeedMoreInfo) with atomic wallet/transaction refund.
    /// </summary>
    [HttpPost("{disputeId:int}/decision")]
    [ProducesResponseType(typeof(SupportApiResponse<DisputeAdminDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SupportApiResponse<DisputeAdminDto>>> MakeDecision(
        int disputeId,
        [FromBody] DisputeDecisionRequestDto request)
    {
        var adminId = _currentUserService.UserId;
        if (!adminId.HasValue) return Unauthorized();

        try
        {
            var result = await _disputeService.MakeDecisionAsync(disputeId, adminId.Value, request);
            return Ok(new SupportApiResponse<DisputeAdminDto>
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = $"Dispute decision '{request.Decision}' executed successfully",
                Data = result
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new SupportApiResponse<object>
            {
                Code = StatusCodes.Status404NotFound,
                Success = false,
                Message = ex.Message
            });
        }
    }

    /// <summary>
    /// A-28: Assign dispute investigation to team/agent.
    /// </summary>
    [HttpPost("{disputeId:int}/assign")]
    [ProducesResponseType(typeof(SupportApiResponse<DisputeAdminDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SupportApiResponse<DisputeAdminDto>>> AssignDispute(
        int disputeId,
        [FromBody] AssignDisputeRequestDto request)
    {
        var adminId = _currentUserService.UserId;
        if (!adminId.HasValue) return Unauthorized();

        try
        {
            var result = await _disputeService.AssignDisputeAsync(disputeId, adminId.Value, request);
            return Ok(new SupportApiResponse<DisputeAdminDto>
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Dispute assigned successfully",
                Data = result
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new SupportApiResponse<object>
            {
                Code = StatusCodes.Status404NotFound,
                Success = false,
                Message = ex.Message
            });
        }
    }
}
