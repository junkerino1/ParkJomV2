using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkJomV2.DTOs;
using ParkJomV2.Services;
using ParkJomV2.Services.Support;

namespace ParkJomV2.Controllers.Support;

[ApiController]
[Route("api/support/conversations")]
[Authorize]
public class SupportConversationController : ControllerBase
{
    private readonly ConversationService _conversationService;
    private readonly CurrentUserService _currentUserService;

    public SupportConversationController(ConversationService conversationService, CurrentUserService currentUserService)
    {
        _conversationService = conversationService;
        _currentUserService = currentUserService;
    }

    /// <summary>
    /// U-06: Start a new Live Chat conversation.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(SupportApiResponse<ConversationDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<SupportApiResponse<ConversationDto>>> CreateConversation([FromBody] CreateConversationRequestDto request)
    {
        var userId = _currentUserService.UserId;
        if (!userId.HasValue) return Unauthorized();

        var conv = await _conversationService.CreateConversationAsync(userId.Value, request);
        return Ok(new SupportApiResponse<ConversationDto>
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Conversation started successfully",
            Data = conv
        });
    }

    /// <summary>
    /// U-07: Get my conversations list.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(SupportApiResponse<List<ConversationDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<SupportApiResponse<List<ConversationDto>>>> GetMyConversations([FromQuery] string? status = null)
    {
        var userId = _currentUserService.UserId;
        if (!userId.HasValue) return Unauthorized();

        var list = await _conversationService.GetMyConversationsAsync(userId.Value, status);
        return Ok(new SupportApiResponse<List<ConversationDto>>
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Conversations retrieved successfully",
            Data = list
        });
    }

    /// <summary>
    /// U-08: Get conversation detail and message history.
    /// </summary>
    [HttpGet("{conversationId:int}")]
    [ProducesResponseType(typeof(SupportApiResponse<ConversationDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SupportApiResponse<ConversationDetailDto>>> GetConversation(int conversationId)
    {
        var user = await _currentUserService.GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var isAdmin = user.UserType == Models.Enums.UserType.Admin;
        var detail = await _conversationService.GetConversationDetailAsync(conversationId, user.UserId, isAdmin);

        if (detail == null)
        {
            return NotFound(new SupportApiResponse<object>
            {
                Code = StatusCodes.Status404NotFound,
                Success = false,
                Message = "Conversation not found"
            });
        }

        return Ok(new SupportApiResponse<ConversationDetailDto>
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Conversation retrieved successfully",
            Data = detail
        });
    }

    /// <summary>
    /// U-09: Send customer message and attachments to conversation.
    /// </summary>
    [HttpPost("{conversationId:int}/messages")]
    [Consumes("multipart/form-data", "application/json")]
    [ProducesResponseType(typeof(SupportApiResponse<ConversationMessageDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<SupportApiResponse<ConversationMessageDto>>> SendMessage(
        int conversationId,
        [FromForm] SendConversationMessageRequestDto request)
    {
        var userId = _currentUserService.UserId;
        if (!userId.HasValue) return Unauthorized();

        var message = await _conversationService.AddMessageAsync(conversationId, userId.Value, "Customer", request);
        return Ok(new SupportApiResponse<ConversationMessageDto>
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Message sent successfully",
            Data = message
        });
    }

    /// <summary>
    /// U-10: Close conversation by customer.
    /// </summary>
    [HttpPost("{conversationId:int}/close")]
    [ProducesResponseType(typeof(SupportApiResponse<ConversationDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<SupportApiResponse<ConversationDto>>> CloseConversation(
        int conversationId,
        [FromBody] CloseConversationRequestDto? request = null)
    {
        var userId = _currentUserService.UserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _conversationService.CloseConversationAsync(conversationId, userId.Value, "Customer", request ?? new CloseConversationRequestDto());
        return Ok(new SupportApiResponse<ConversationDto>
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Conversation closed successfully",
            Data = result
        });
    }

    /// <summary>
    /// U-11: Convert conversation into a tracked Support Ticket.
    /// </summary>
    [HttpPost("{conversationId:int}/ticket")]
    [ProducesResponseType(typeof(SupportApiResponse<SupportTicketDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<SupportApiResponse<SupportTicketDto>>> ConvertToTicket(
        int conversationId,
        [FromBody] ConvertConversationToTicketRequestDto request)
    {
        var user = await _currentUserService.GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var role = user.UserType == Models.Enums.UserType.Admin ? "Admin" : "Customer";
        var ticket = await _conversationService.ConvertToTicketAsync(conversationId, user.UserId, role, request);

        return Ok(new SupportApiResponse<SupportTicketDto>
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Conversation converted to ticket successfully",
            Data = ticket
        });
    }
}
