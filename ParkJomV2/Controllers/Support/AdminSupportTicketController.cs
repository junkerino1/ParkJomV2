using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkJomV2.DTOs;
using ParkJomV2.Services;
using ParkJomV2.Services.Support;

namespace ParkJomV2.Controllers.Support;

[ApiController]
[Route("api/admin/support/tickets")]
[Authorize(Policy = "AdminOnly")]
public class AdminSupportTicketController : ControllerBase
{
    private readonly SupportTicketService _ticketService;
    private readonly CurrentUserService _currentUserService;

    public AdminSupportTicketController(SupportTicketService ticketService, CurrentUserService currentUserService)
    {
        _ticketService = ticketService;
        _currentUserService = currentUserService;
    }

    /// <summary>
    /// A-07: Admin support ticket queue (frontend compatible: returns data array if requested, or paged).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(SupportApiResponse<List<SupportTicketSummaryDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<SupportApiResponse<List<SupportTicketSummaryDto>>>> GetTickets(
        [FromQuery] string? status = null,
        [FromQuery] string? priority = null,
        [FromQuery] string? team = null,
        [FromQuery] int? assignee = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100)
    {
        var (items, total) = await _ticketService.GetAdminTicketsAsync(status, priority, team, assignee, search, page, pageSize);
        return Ok(new SupportApiResponse<List<SupportTicketSummaryDto>>
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Support tickets retrieved successfully",
            Data = items
        });
    }

    /// <summary>
    /// A-08: Full admin ticket detail with internal notes and audit timeline.
    /// </summary>
    [HttpGet("{ticketIdentifier}")]
    [ProducesResponseType(typeof(SupportApiResponse<SupportTicketDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SupportApiResponse<SupportTicketDto>>> GetTicketDetail(string ticketIdentifier)
    {
        var adminId = _currentUserService.UserId;
        if (!adminId.HasValue) return Unauthorized();

        var ticket = await _ticketService.GetTicketDetailByIdentifierAsync(ticketIdentifier, adminId.Value, true);
        if (ticket == null)
        {
            return NotFound(new SupportApiResponse<object>
            {
                Code = StatusCodes.Status404NotFound,
                Success = false,
                Message = "Ticket not found"
            });
        }

        return Ok(new SupportApiResponse<SupportTicketDto>
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Ticket details retrieved successfully",
            Data = ticket
        });
    }

    /// <summary>
    /// A-09: Admin creates custom ticket manually (frontend compatible).
    /// </summary>
    [HttpPost]
    [Consumes("multipart/form-data", "application/json")]
    [ProducesResponseType(typeof(SupportApiResponse<SupportTicketDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<SupportApiResponse<SupportTicketDto>>> CreateTicket([FromForm] AdminCreateSupportTicketRequestDto request)
    {
        var adminId = _currentUserService.UserId;
        if (!adminId.HasValue) return Unauthorized();

        var ticket = await _ticketService.AdminCreateTicketAsync(adminId.Value, request);
        return Ok(new SupportApiResponse<SupportTicketDto>
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Custom ticket created successfully",
            Data = ticket
        });
    }

    /// <summary>
    /// A-10: Accept ticket by admin (frontend compatible).
    /// </summary>
    [HttpPost("{ticketId:int}/accept")]
    [ProducesResponseType(typeof(SupportApiResponse<SupportTicketDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SupportApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SupportApiResponse<SupportTicketDto>>> AcceptTicket(int ticketId)
    {
        var adminId = _currentUserService.UserId;
        if (!adminId.HasValue) return Unauthorized();

        try
        {
            var ticket = await _ticketService.AcceptTicketAsync(ticketId, adminId.Value);
            return Ok(new SupportApiResponse<SupportTicketDto>
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Ticket accepted successfully",
                Data = ticket
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
    /// A-11: Assign ticket to admin user or department team.
    /// </summary>
    [HttpPost("{ticketId:int}/assign")]
    [ProducesResponseType(typeof(SupportApiResponse<SupportTicketDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SupportApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SupportApiResponse<SupportTicketDto>>> AssignTicket(
        int ticketId,
        [FromBody] AssignTicketRequestDto request)
    {
        var adminId = _currentUserService.UserId;
        if (!adminId.HasValue) return Unauthorized();

        try
        {
            var ticket = await _ticketService.AssignTicketAsync(ticketId, adminId.Value, request);
            return Ok(new SupportApiResponse<SupportTicketDto>
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Ticket assigned successfully",
                Data = ticket
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
    /// A-12: Transition ticket status with state machine verification.
    /// </summary>
    [HttpPost("{ticketId:int}/transition")]
    [ProducesResponseType(typeof(SupportApiResponse<SupportTicketDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SupportApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(SupportApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SupportApiResponse<SupportTicketDto>>> TransitionTicket(
        int ticketId,
        [FromBody] TicketTransitionRequestDto request)
    {
        var adminId = _currentUserService.UserId;
        if (!adminId.HasValue) return Unauthorized();

        try
        {
            var ticket = await _ticketService.TransitionStatusAsync(ticketId, adminId.Value, "Admin", request);
            return Ok(new SupportApiResponse<SupportTicketDto>
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Ticket status updated successfully",
                Data = ticket
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
    /// A-13: Close ticket (frontend compatible shortcut).
    /// </summary>
    [HttpPost("{ticketId:int}/close")]
    [ProducesResponseType(typeof(SupportApiResponse<SupportTicketDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SupportApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(SupportApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SupportApiResponse<SupportTicketDto>>> CloseTicket(
        int ticketId,
        [FromBody] CloseConversationRequestDto? request = null)
    {
        var adminId = _currentUserService.UserId;
        if (!adminId.HasValue) return Unauthorized();

        try
        {
            var ticket = await _ticketService.TransitionStatusAsync(ticketId, adminId.Value, "Admin", new TicketTransitionRequestDto
            {
                ToStatus = "Closed",
                Reason = request?.Reason ?? "Closed by Admin",
                ResolutionCode = "ClosedByAdmin"
            });

            return Ok(new SupportApiResponse<SupportTicketDto>
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Ticket closed successfully",
                Data = ticket
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
    /// A-14: Link ticket to existing Operational Incident.
    /// </summary>
    [HttpPost("{ticketIdentifier}/link-incident")]
    [ProducesResponseType(typeof(SupportApiResponse<SupportTicketDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SupportApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(SupportApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SupportApiResponse<SupportTicketDto>>> LinkIncident(
        string ticketIdentifier,
        [FromBody] LinkIncidentRequestDto request)
    {
        var adminId = _currentUserService.UserId;
        if (!adminId.HasValue) return Unauthorized();

        try
        {
            var ticket = await _ticketService.LinkIncidentAsync(ticketIdentifier, request, adminId.Value);
            return Ok(new SupportApiResponse<SupportTicketDto>
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Ticket linked with incident successfully",
                Data = ticket
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
    /// A-15: Link ticket to existing Dispute Investigation.
    /// </summary>
    [HttpPost("{ticketIdentifier}/link-dispute")]
    [ProducesResponseType(typeof(SupportApiResponse<SupportTicketDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SupportApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(SupportApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SupportApiResponse<SupportTicketDto>>> LinkDispute(
        string ticketIdentifier,
        [FromBody] LinkDisputeRequestDto request)
    {
        var adminId = _currentUserService.UserId;
        if (!adminId.HasValue) return Unauthorized();

        try
        {
            var ticket = await _ticketService.LinkDisputeAsync(ticketIdentifier, request, adminId.Value);
            return Ok(new SupportApiResponse<SupportTicketDto>
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Ticket linked with dispute successfully",
                Data = ticket
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
