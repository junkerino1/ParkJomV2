using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkJomV2.DTOs;
using ParkJomV2.Services.Support;

namespace ParkJomV2.Controllers.Support;

[ApiController]
[Route("api/admin/support")]
[Authorize(Policy = "AdminOnly")]
public class AdminSupportDashboardController : ControllerBase
{
    private readonly SupportOnCallService _onCallService;

    public AdminSupportDashboardController(SupportOnCallService onCallService)
    {
        _onCallService = onCallService;
    }

    /// <summary>
    /// A-01: Command center dashboard metrics (waiting conversations, open tickets, active incidents, disputes, SLA risk).
    /// </summary>
    [HttpGet("dashboard")]
    [ProducesResponseType(typeof(SupportApiResponse<SupportDashboardDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<SupportApiResponse<SupportDashboardDto>>> GetDashboardMetrics()
    {
        var metrics = await _onCallService.GetDashboardMetricsAsync();
        return Ok(new SupportApiResponse<SupportDashboardDto>
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Dashboard metrics retrieved successfully",
            Data = metrics
        });
    }

    /// <summary>
    /// A-32: View append-only support audit event timeline.
    /// </summary>
    [HttpGet("audit")]
    [ProducesResponseType(typeof(SupportApiPagedResponse<SupportAuditEventDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<SupportApiPagedResponse<SupportAuditEventDto>>> GetAuditLogs(
        [FromQuery] string? objectType = null,
        [FromQuery] int? objectId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var (items, total) = await _onCallService.GetAuditLogsAsync(objectType, objectId, page, pageSize);
        return Ok(new SupportApiPagedResponse<SupportAuditEventDto>
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Audit logs retrieved successfully",
            Data = new PagedResult<SupportAuditEventDto>
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalCount = total
            }
        });
    }
}
