using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkJomV2.DTOs;
using ParkJomV2.Services;
using ParkJomV2.Services.Support;

namespace ParkJomV2.Controllers.Support;

[ApiController]
[Route("api/admin/support/conversations")]
[Authorize(Policy = "AdminOnly")]
public class AdminSupportConversationController : ControllerBase
{
    private readonly ConversationService _conversationService;
    private readonly SupportWorkflowService _workflowService;
    private readonly CurrentUserService _currentUserService;

    public AdminSupportConversationController(
        ConversationService conversationService,
        SupportWorkflowService workflowService,
        CurrentUserService currentUserService)
    {
        _conversationService = conversationService;
        _workflowService = workflowService;
        _currentUserService = currentUserService;
    }

    /// <summary>
    /// A-02: Admin conversation queue with filtering and pagination.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(SupportApiPagedResponse<ConversationDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<SupportApiPagedResponse<ConversationDto>>> GetConversations(
        [FromQuery] string? status = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        var (items, total) = await _conversationService.GetAdminConversationsAsync(status, search, page, pageSize);
        return Ok(new SupportApiPagedResponse<ConversationDto>
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Conversations retrieved successfully",
            Data = new PagedResult<ConversationDto>
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalCount = total
            }
        });
    }

    /// <summary>
    /// A-03: Admin reply or internal note.
    /// </summary>
    [HttpPost("{conversationId:int}/messages")]
    [Consumes("multipart/form-data", "application/json")]
    [ProducesResponseType(typeof(SupportApiResponse<ConversationMessageDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<SupportApiResponse<ConversationMessageDto>>> SendAdminMessage(
        int conversationId,
        [FromForm] SendConversationMessageRequestDto request)
    {
        var adminId = _currentUserService.UserId;
        if (!adminId.HasValue) return Unauthorized();

        var message = await _conversationService.AddMessageAsync(conversationId, adminId.Value, "Admin", request);
        return Ok(new SupportApiResponse<ConversationMessageDto>
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Admin message posted successfully",
            Data = message
        });
    }

    /// <summary>
    /// A-04: Reply and close conversation by admin.
    /// </summary>
    [HttpPost("{conversationId:int}/close")]
    [ProducesResponseType(typeof(SupportApiResponse<ConversationDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<SupportApiResponse<ConversationDto>>> CloseConversation(
        int conversationId,
        [FromBody] CloseConversationRequestDto? request = null)
    {
        var adminId = _currentUserService.UserId;
        if (!adminId.HasValue) return Unauthorized();

        var result = await _conversationService.CloseConversationAsync(conversationId, adminId.Value, "Admin", request ?? new CloseConversationRequestDto());
        return Ok(new SupportApiResponse<ConversationDto>
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Conversation closed by admin",
            Data = result
        });
    }

    /// <summary>
    /// A-05: Direct customer in conversation to a preset Quick Help workflow.
    /// </summary>
    [HttpPost("{conversationId:int}/workflow")]
    [ProducesResponseType(typeof(SupportApiResponse<ConversationMessageDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<SupportApiResponse<ConversationMessageDto>>> PushWorkflow(
        int conversationId,
        [FromBody] WorkflowQuestionOptionDto workflowTarget)
    {
        var adminId = _currentUserService.UserId;
        if (!adminId.HasValue) return Unauthorized();

        var def = _workflowService.GetWorkflowDefinition(workflowTarget.Key);
        var messageText = $"[Quick Help Recommendation] Please use our automated {def?.Title ?? workflowTarget.Key} workflow to resolve this case quickly.";

        var msg = await _conversationService.AddMessageAsync(conversationId, adminId.Value, "Admin", new SendConversationMessageRequestDto
        {
            Message = messageText,
            IsInternal = false
        });

        return Ok(new SupportApiResponse<ConversationMessageDto>
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Workflow recommendation posted to conversation",
            Data = msg
        });
    }

    /// <summary>
    /// A-06: Create tracked Custom Ticket from Conversation.
    /// </summary>
    [HttpPost("{conversationId:int}/ticket")]
    [ProducesResponseType(typeof(SupportApiResponse<SupportTicketDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<SupportApiResponse<SupportTicketDto>>> CreateTicketFromConversation(
        int conversationId,
        [FromBody] ConvertConversationToTicketRequestDto request)
    {
        var adminId = _currentUserService.UserId;
        if (!adminId.HasValue) return Unauthorized();

        var ticket = await _conversationService.ConvertToTicketAsync(conversationId, adminId.Value, "Admin", request);
        return Ok(new SupportApiResponse<SupportTicketDto>
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Custom ticket created from conversation successfully",
            Data = ticket
        });
    }
}
