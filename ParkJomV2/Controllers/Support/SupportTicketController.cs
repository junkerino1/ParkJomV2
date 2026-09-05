using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Services;
using ParkJomV2.Services.Support;

namespace ParkJomV2.Controllers.Support;

[ApiController]
[Route("api/support")]
[Authorize]
public class SupportTicketController : ControllerBase
{
    private readonly SupportTicketService _ticketService;
    private readonly CurrentUserService _currentUserService;
    private readonly ApplicationDbContext _context;

    public SupportTicketController(
        SupportTicketService ticketService,
        CurrentUserService currentUserService,
        ApplicationDbContext context)
    {
        _ticketService = ticketService;
        _currentUserService = currentUserService;
        _context = context;
    }

    /// <summary>
    /// U-12: List my support tickets (frontend compatible).
    /// </summary>
    [HttpGet("tickets/mine")]
    [ProducesResponseType(typeof(SupportApiResponse<List<SupportTicketSummaryDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<SupportApiResponse<List<SupportTicketSummaryDto>>>> GetMyTickets(
        [FromQuery] string? status = null,
        [FromQuery] string? search = null)
    {
        var userId = _currentUserService.UserId;
        if (!userId.HasValue) return Unauthorized();

        var tickets = await _ticketService.GetMyTicketsAsync(userId.Value, status, search);
        return Ok(new SupportApiResponse<List<SupportTicketSummaryDto>>
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "My tickets retrieved successfully",
            Data = tickets
        });
    }

    /// <summary>
    /// U-13: Get support ticket details, messages, and attachments.
    /// </summary>
    [HttpGet("tickets/{ticketId:int}")]
    [ProducesResponseType(typeof(SupportApiResponse<SupportTicketDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SupportApiResponse<SupportTicketDto>>> GetTicket(int ticketId)
    {
        var user = await _currentUserService.GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var isAdmin = user.UserType == Models.Enums.UserType.Admin;
        var ticket = await _ticketService.GetTicketDetailAsync(ticketId, user.UserId, isAdmin);

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
    /// U-14: Create a new support ticket (frontend compatible, multipart/form-data or JSON).
    /// </summary>
    [HttpPost("tickets")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(SupportApiResponse<SupportTicketDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<SupportApiResponse<SupportTicketDto>>> CreateTicket([FromForm] CreateSupportTicketRequestDto request)
        var userId = _currentUserService.UserId;
        if (!userId.HasValue) return Unauthorized();

        var ticket = await _ticketService.CreateCustomerTicketAsync(userId.Value, request);
        return Ok(new SupportApiResponse<SupportTicketDto>
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Support ticket created successfully",
            Data = ticket
        });
    }

    /// <summary>
    /// U-15: Send a message/reply to a ticket (frontend compatible).
    /// </summary>
    [HttpPost("tickets/{ticketId:int}/messages")]
    [Consumes("multipart/form-data", "application/json")]
    [ProducesResponseType(typeof(SupportApiResponse<SupportTicketMessageDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<SupportApiResponse<SupportTicketMessageDto>>> SendTicketMessage(
        int ticketId,
        [FromForm] SendTicketMessageRequestDto request)
    {
        var user = await _currentUserService.GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var role = user.UserType == Models.Enums.UserType.Admin ? "Admin" : "Customer";
        var message = await _ticketService.AddMessageAsync(ticketId, user.UserId, role, request);

        return Ok(new SupportApiResponse<SupportTicketMessageDto>
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Message sent successfully",
            Data = message
        });
    }

    /// <summary>
    /// U-16: Reopen a resolved or closed ticket.
    /// </summary>
    [HttpPost("tickets/{ticketId:int}/reopen")]
    [ProducesResponseType(typeof(SupportApiResponse<SupportTicketDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<SupportApiResponse<SupportTicketDto>>> ReopenTicket(
        int ticketId,
        [FromBody] ReopenTicketRequestDto request)
    {
        var user = await _currentUserService.GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var role = user.UserType == Models.Enums.UserType.Admin ? "Admin" : "Customer";
        var ticket = await _ticketService.ReopenTicketAsync(ticketId, user.UserId, role, request);

        return Ok(new SupportApiResponse<SupportTicketDto>
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Ticket reopened successfully",
            Data = ticket
        });
    }

    /// <summary>
    /// U-20: Download or access authorized private support attachment.
    /// </summary>
    [HttpGet("attachments/{attachmentId:int}")]
    [ProducesResponseType(typeof(SupportApiResponse<SupportAttachmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SupportApiResponse<SupportAttachmentDto>>> GetAttachment(int attachmentId)
    {
        var user = await _currentUserService.GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var attachment = await _context.SupportAttachments
            .Include(a => a.Ticket)
            .Include(a => a.Conversation)
            .FirstOrDefaultAsync(a => a.AttachmentId == attachmentId);

        if (attachment == null)
        {
            return NotFound(new SupportApiResponse<object>
            {
                Code = StatusCodes.Status404NotFound,
                Success = false,
                Message = "Attachment not found"
            });
        }

        // Authorization check
        var isAdmin = user.UserType == Models.Enums.UserType.Admin;
        var isOwner = attachment.UploadedByUserId == user.UserId
                      || (attachment.Ticket != null && attachment.Ticket.CustomerUserId == user.UserId)
                      || (attachment.Conversation != null && attachment.Conversation.CustomerUserId == user.UserId);

        if (!isAdmin && !isOwner)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new SupportApiResponse<object>
            {
                Code = StatusCodes.Status403Forbidden,
                Success = false,
                Message = "You do not have permission to view this attachment"
            });
        }

        return Ok(new SupportApiResponse<SupportAttachmentDto>
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Attachment retrieved successfully",
            Data = new SupportAttachmentDto
            {
                AttachmentId = attachment.AttachmentId,
                FileName = attachment.FileName,
                FileUrl = attachment.FileUrl,
                ContentType = attachment.ContentType,
                FileSize = attachment.FileSize,
                IsPrivate = attachment.IsPrivate,
                CreatedAt = attachment.CreatedAt
            }
        });
    }
}
