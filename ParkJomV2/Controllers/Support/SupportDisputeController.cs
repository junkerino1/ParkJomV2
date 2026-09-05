using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkJomV2.DTOs;
using ParkJomV2.Services;
using ParkJomV2.Services.Support;

namespace ParkJomV2.Controllers.Support;

[ApiController]
[Route("api/support/disputes")]
[Authorize]
public class SupportDisputeController : ControllerBase
{
    private readonly DisputeService _disputeService;
    private readonly CurrentUserService _currentUserService;

    public SupportDisputeController(DisputeService disputeService, CurrentUserService currentUserService)
    {
        _disputeService = disputeService;
        _currentUserService = currentUserService;
    }

    /// <summary>
    /// U-17: Get my dispute investigations list.
    /// </summary>
    [HttpGet("mine")]
    [ProducesResponseType(typeof(SupportApiResponse<List<DisputeCustomerSummaryDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<SupportApiResponse<List<DisputeCustomerSummaryDto>>>> GetMyDisputes([FromQuery] string? status = null)
    {
        var userId = _currentUserService.UserId;
        if (!userId.HasValue) return Unauthorized();

        var disputes = await _disputeService.GetMyDisputesAsync(userId.Value, status);
        return Ok(new SupportApiResponse<List<DisputeCustomerSummaryDto>>
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "My disputes retrieved successfully",
            Data = disputes
        });
    }

    /// <summary>
    /// U-18: Get customer-safe dispute details and investigation progress.
    /// </summary>
    [HttpGet("{disputeId:int}")]
    [ProducesResponseType(typeof(SupportApiResponse<DisputeCustomerDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SupportApiResponse<DisputeCustomerDto>>> GetDispute(int disputeId)
    {
        var userId = _currentUserService.UserId;
        if (!userId.HasValue) return Unauthorized();

        var dispute = await _disputeService.GetDisputeDetailForCustomerAsync(disputeId, userId.Value);
        if (dispute == null)
        {
            return NotFound(new SupportApiResponse<object>
            {
                Code = StatusCodes.Status404NotFound,
                Success = false,
                Message = "Dispute case not found"
            });
        }

        return Ok(new SupportApiResponse<DisputeCustomerDto>
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Dispute case retrieved successfully",
            Data = dispute
        });
    }

    /// <summary>
    /// U-19: Upload customer evidence / document for dispute investigation.
    /// </summary>
    [HttpPost("{disputeId:int}/evidence")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(SupportApiResponse<DisputeEvidenceDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<SupportApiResponse<DisputeEvidenceDto>>> UploadEvidence(
        int disputeId,
        [FromForm] UploadDisputeEvidenceRequestDto request)
    {
        var userId = _currentUserService.UserId;
        if (!userId.HasValue) return Unauthorized();

        var evidence = await _disputeService.UploadEvidenceAsync(disputeId, userId.Value, "Customer", request);
        return Ok(new SupportApiResponse<DisputeEvidenceDto>
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Evidence uploaded successfully",
            Data = evidence
        });
    }
}
