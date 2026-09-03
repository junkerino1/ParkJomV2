using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkJomV2.DTOs;
using ParkJomV2.Services;
using ParkJomV2.Services.Support;

namespace ParkJomV2.Controllers.Support;

[ApiController]
[Route("api/support")]
[Authorize]
public class SupportWorkflowController : ControllerBase
{
    private readonly SupportWorkflowService _workflowService;
    private readonly CurrentUserService _currentUserService;

    public SupportWorkflowController(SupportWorkflowService workflowService, CurrentUserService currentUserService)
    {
        _workflowService = workflowService;
        _currentUserService = currentUserService;
    }

    /// <summary>
    /// U-02: Get all enabled Quick Help workflow definitions.
    /// </summary>
    [HttpGet("workflows")]
    [ProducesResponseType(typeof(SupportApiResponse<List<SupportWorkflowDefinitionDto>>), StatusCodes.Status200OK)]
    public ActionResult<SupportApiResponse<List<SupportWorkflowDefinitionDto>>> GetWorkflows()
    {
        var workflows = _workflowService.GetWorkflowDefinitions();
        return Ok(new SupportApiResponse<List<SupportWorkflowDefinitionDto>>
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Workflows retrieved successfully",
            Data = workflows
        });
    }

    /// <summary>
    /// U-03: Get a specific workflow's question schema and allowed options.
    /// </summary>
    [HttpGet("workflows/{workflowKey}")]
    [ProducesResponseType(typeof(SupportApiResponse<SupportWorkflowDefinitionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<SupportApiResponse<SupportWorkflowDefinitionDto>> GetWorkflow(string workflowKey)
    {
        var workflow = _workflowService.GetWorkflowDefinition(workflowKey);
        if (workflow == null)
        {
            return NotFound(new SupportApiResponse<object>
            {
                Code = StatusCodes.Status404NotFound,
                Success = false,
                Message = $"Workflow '{workflowKey}' not found"
            });
        }

        return Ok(new SupportApiResponse<SupportWorkflowDefinitionDto>
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Workflow retrieved successfully",
            Data = workflow
        });
    }

    /// <summary>
    /// U-04: Execute a Quick Help workflow run with answers and automatic triage.
    /// </summary>
    [HttpPost("workflows/{workflowKey}/runs")]
    [ProducesResponseType(typeof(SupportApiResponse<WorkflowRunResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SupportApiResponse<WorkflowRunResultDto>>> ExecuteWorkflowRun(
        string workflowKey,
        [FromBody] ExecuteWorkflowRunRequestDto request)
    {
        var userId = _currentUserService.UserId;
        if (!userId.HasValue) return Unauthorized();

        try
        {
            var result = await _workflowService.ExecuteWorkflowRunAsync(userId.Value, workflowKey, request);
            return Ok(new SupportApiResponse<WorkflowRunResultDto>
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Workflow executed successfully",
                Data = result
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
    /// U-05: Get status and outcome of an asynchronous or prior workflow run.
    /// </summary>
    [HttpGet("workflow-runs/{runId:int}")]
    [ProducesResponseType(typeof(SupportApiResponse<WorkflowRunResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SupportApiResponse<WorkflowRunResultDto>>> GetWorkflowRun(int runId)
    {
        var user = await _currentUserService.GetCurrentUserAsync();
        if (user == null) return Unauthorized();

        var isAdmin = user.UserType == Models.Enums.UserType.Admin;
        var result = await _workflowService.GetWorkflowRunResultAsync(runId, user.UserId, isAdmin);

        if (result == null)
        {
            return NotFound(new SupportApiResponse<object>
            {
                Code = StatusCodes.Status404NotFound,
                Success = false,
                Message = "Workflow run not found"
            });
        }

        return Ok(new SupportApiResponse<WorkflowRunResultDto>
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Workflow run retrieved successfully",
            Data = result
        });
    }
}
