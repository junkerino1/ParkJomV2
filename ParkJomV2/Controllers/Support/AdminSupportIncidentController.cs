using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkJomV2.DTOs;
using ParkJomV2.Services;
using ParkJomV2.Services.Support;

namespace ParkJomV2.Controllers.Support;

[ApiController]
[Route("api/admin/support/incidents")]
[Authorize(Policy = "AdminOnly")]
public class AdminSupportIncidentController : ControllerBase
{
    private readonly IncidentService _incidentService;
    private readonly CurrentUserService _currentUserService;

    public AdminSupportIncidentController(IncidentService incidentService, CurrentUserService currentUserService)
    {
        _incidentService = incidentService;
        _currentUserService = currentUserService;
    }

    /// <summary>
    /// A-16: Get operational incidents list.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(SupportApiPagedResponse<OperationalIncidentSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<SupportApiPagedResponse<OperationalIncidentSummaryDto>>> GetIncidents(
        [FromQuery] string? status = null,
        [FromQuery] string? priority = null,
        [FromQuery] string? team = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        var (items, total) = await _incidentService.GetIncidentsAsync(status, priority, team, page, pageSize);
        return Ok(new SupportApiPagedResponse<OperationalIncidentSummaryDto>
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Incidents retrieved successfully",
            Data = new PagedResult<OperationalIncidentSummaryDto>
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalCount = total
            }
        });
    }

    /// <summary>
    /// A-17: Get operational incident full detail, linked tickets, and escalation status (by incidentId or reference e.g. INC-2026-52563).
    /// </summary>
    [HttpGet("{incidentIdentifier}")]
    [ProducesResponseType(typeof(SupportApiResponse<OperationalIncidentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SupportApiResponse<OperationalIncidentDto>>> GetIncident(string incidentIdentifier)
    {
        var incident = await _incidentService.GetIncidentDetailByIdentifierAsync(incidentIdentifier);
        if (incident == null)
        {
            return NotFound(new SupportApiResponse<object>
            {
                Code = StatusCodes.Status404NotFound,
                Success = false,
                Message = "Incident not found"
            });
        }

        return Ok(new SupportApiResponse<OperationalIncidentDto>
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Incident details retrieved successfully",
            Data = incident
        });
    }

    /// <summary>
    /// A-18: Manually create an operational incident.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(SupportApiResponse<OperationalIncidentDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<SupportApiResponse<OperationalIncidentDto>>> CreateIncident([FromBody] CreateIncidentRequestDto request)
    {
        var adminId = _currentUserService.UserId;
        if (!adminId.HasValue) return Unauthorized();

        var incident = await _incidentService.CreateIncidentAsync(adminId.Value, request);
        return Ok(new SupportApiResponse<OperationalIncidentDto>
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Operational incident created successfully",
            Data = incident
        });
    }

    /// <summary>
    /// A-19: Acknowledge incident (stops P0/P1 on-call escalation timer).
    /// </summary>
    [HttpPost("{incidentId:int}/acknowledge")]
    [ProducesResponseType(typeof(SupportApiResponse<OperationalIncidentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SupportApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SupportApiResponse<OperationalIncidentDto>>> AcknowledgeIncident(int incidentId)
    {
        var adminId = _currentUserService.UserId;
        if (!adminId.HasValue) return Unauthorized();

        try
        {
            var incident = await _incidentService.AcknowledgeIncidentAsync(incidentId, adminId.Value);
            return Ok(new SupportApiResponse<OperationalIncidentDto>
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Incident acknowledged successfully",
                Data = incident
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
    /// A-20: Assign incident to responder or operations team.
    /// </summary>
    [HttpPost("{incidentId:int}/assign")]
    [ProducesResponseType(typeof(SupportApiResponse<OperationalIncidentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SupportApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SupportApiResponse<OperationalIncidentDto>>> AssignIncident(
        int incidentId,
        [FromBody] AssignIncidentRequestDto request)
    {
        var adminId = _currentUserService.UserId;
        if (!adminId.HasValue) return Unauthorized();

        try
        {
            var incident = await _incidentService.AssignIncidentAsync(incidentId, adminId.Value, request);
            return Ok(new SupportApiResponse<OperationalIncidentDto>
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Incident assigned successfully",
                Data = incident
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
    /// A-21: Transition incident status (Monitoring, Resolved, Closed).
    /// </summary>
    [HttpPost("{incidentId:int}/transition")]
    [ProducesResponseType(typeof(SupportApiResponse<OperationalIncidentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SupportApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(SupportApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SupportApiResponse<OperationalIncidentDto>>> TransitionIncident(
        int incidentId,
        [FromBody] IncidentTransitionRequestDto request)
    {
        var adminId = _currentUserService.UserId;
        if (!adminId.HasValue) return Unauthorized();

        try
        {
            var incident = await _incidentService.TransitionStatusAsync(incidentId, adminId.Value, request);
            return Ok(new SupportApiResponse<OperationalIncidentDto>
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Incident status updated successfully",
                Data = incident
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
        catch (ArgumentException ex)
        {
            return BadRequest(new SupportApiResponse<object>
            {
                Code = StatusCodes.Status400BadRequest,
                Success = false,
                Message = ex.Message
            });
        }
    }

    /// <summary>
    /// A-22: Execute an audited remote gate/barrier access override for a booking.
    /// </summary>
    [HttpPost("{incidentId:int}/access-override")]
    [ProducesResponseType(typeof(SupportApiResponse<AccessOverrideResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SupportApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(SupportApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SupportApiResponse<AccessOverrideResultDto>>> AccessOverride(
        int incidentId,
        [FromBody] AccessOverrideRequestDto request)
    {
        var adminId = _currentUserService.UserId;
        if (!adminId.HasValue) return Unauthorized();

        try
        {
            var result = await _incidentService.ExecuteAccessOverrideAsync(incidentId, adminId.Value, request);
            return Ok(new SupportApiResponse<AccessOverrideResultDto>
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Barrier access override command executed and audited",
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
        catch (ArgumentException ex)
        {
            return BadRequest(new SupportApiResponse<object>
            {
                Code = StatusCodes.Status400BadRequest,
                Success = false,
                Message = ex.Message
            });
        }
    }

    /// <summary>
    /// Link ticket to operational incident by ID or reference (e.g. TKT-2026-35442).
    /// </summary>
    [HttpPost("{incidentIdentifier}/link-ticket")]
    [ProducesResponseType(typeof(SupportApiResponse<OperationalIncidentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SupportApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(SupportApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SupportApiResponse<OperationalIncidentDto>>> LinkTicket(
        string incidentIdentifier,
        [FromBody] LinkTicketRequestDto request)
    {
        var adminId = _currentUserService.UserId;
        if (!adminId.HasValue) return Unauthorized();

        try
        {
            var incident = await _incidentService.LinkTicketAsync(incidentIdentifier, request, adminId.Value);
            return Ok(new SupportApiResponse<OperationalIncidentDto>
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Ticket linked with incident successfully",
                Data = incident
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
        catch (ArgumentException ex)
        {
            return BadRequest(new SupportApiResponse<object>
            {
                Code = StatusCodes.Status400BadRequest,
                Success = false,
                Message = ex.Message
            });
        }
    }
}
