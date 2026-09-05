using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkJomV2.DTOs;
using ParkJomV2.Services;
using ParkJomV2.Services.Support;

namespace ParkJomV2.Controllers.Support;

[ApiController]
[Route("api/admin/support/on-call")]
[Authorize(Policy = "AdminOnly")]
public class AdminSupportOnCallController : ControllerBase
{
    private readonly SupportOnCallService _onCallService;
    private readonly CurrentUserService _currentUserService;

    public AdminSupportOnCallController(SupportOnCallService onCallService, CurrentUserService currentUserService)
    {
        _onCallService = onCallService;
        _currentUserService = currentUserService;
    }

    /// <summary>
    /// A-29: Get current 24/7 on-call shift schedule, responders, active channels, and policy.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(SupportApiResponse<SupportOnCallStatusDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<SupportApiResponse<SupportOnCallStatusDto>>> GetOnCallStatus()
    {
        var status = await _onCallService.GetOnCallStatusAsync();
        return Ok(new SupportApiResponse<SupportOnCallStatusDto>
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "On-call status retrieved successfully",
            Data = status
        });
    }

    /// <summary>
    /// A-30: Test on-call notification provider (Push, SMS, Phone, Email) without real incident.
    /// </summary>
    [HttpPost("test")]
    [ProducesResponseType(typeof(SupportApiResponse<TestNotificationResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<SupportApiResponse<TestNotificationResultDto>>> TestNotification([FromBody] TestOnCallNotificationRequestDto request)
    {
        var adminId = _currentUserService.UserId;
        if (!adminId.HasValue) return Unauthorized();

        var result = await _onCallService.TestNotificationAsync(adminId.Value, request);
        return Ok(new SupportApiResponse<TestNotificationResultDto>
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "Notification test completed",
            Data = result
        });
    }

    /// <summary>
    /// A-31: Update P0/P1 escalation delay thresholds and notification policy.
    /// </summary>
    [HttpPut("policy")]
    [ProducesResponseType(typeof(SupportApiResponse<SupportOnCallPolicyDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<SupportApiResponse<SupportOnCallPolicyDto>>> UpdatePolicy([FromBody] UpdateOnCallPolicyRequestDto request)
    {
        var adminId = _currentUserService.UserId;
        if (!adminId.HasValue) return Unauthorized();

        var updatedPolicy = await _onCallService.UpdatePolicyAsync(adminId.Value, request);
        return Ok(new SupportApiResponse<SupportOnCallPolicyDto>
        {
            Code = StatusCodes.Status200OK,
            Success = true,
            Message = "On-call policy updated successfully",
            Data = updatedPolicy
        });
    }
}
