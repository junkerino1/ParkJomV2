using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services;

namespace ParkJomV2.Controllers;

[ApiController]
[Authorize(Policy = "AdminOnly")]
[Route("api/admin/owner-suspensions")]
public class OwnerSuspensionsController : ControllerBase
{
    private const string ActiveStatus = "Active";
    private const string SuspendedStatus = "Suspended";

    private readonly ApplicationDbContext _context;
    private readonly AccessLogService _accessLogService;
    private readonly ILogger<OwnerSuspensionsController> _logger;

    public OwnerSuspensionsController(
        ApplicationDbContext context,
        AccessLogService accessLogService,
        ILogger<OwnerSuspensionsController> logger)
    {
        _context = context;
        _accessLogService = accessLogService;
        _logger = logger;
    }

    // GET /api/admin/owner-suspensions
    [HttpGet]
    public async Task<IActionResult> GetSuspendedOwners()
    {
        var suspendedOwners = await _context.Users
            .AsNoTracking()
            .Where(u =>
                u.UserType == UserType.PropertyOwner &&
                u.AccountStatus == SuspendedStatus)
            .OrderByDescending(u => u.UpdatedAt)
            .Select(u => new SuspendedOwnerDTO
            {
                UserId = u.UserId,
                Email = u.Email,
                FirstName = u.FirstName,
                LastName = u.LastName,
                AccountStatus = u.AccountStatus,
                UpdatedAt = u.UpdatedAt
            })
            .ToListAsync();

        return Ok(new
        {
            code = StatusCodes.Status200OK,
            success = true,
            message = "Suspended owners retrieved successfully.",
            data = suspendedOwners
        });
    }

    // POST /api/admin/owner-suspensions/suspend
    [HttpPost("suspend")]
    public async Task<IActionResult> SuspendOwner(
        [FromBody] OwnerSuspensionRequest request)
    {
        var normalizedEmail = request.Email.Trim().ToLower();

        var owner = await _context.Users
            .FirstOrDefaultAsync(u =>
                u.Email.ToLower() == normalizedEmail);

        if (owner == null)
        {
            await _accessLogService.LogAsync(
                User,
                "SuspendOwner",
                false,
                $"Owner not found: {request.Email}");

            return NotFound(new
            {
                code = StatusCodes.Status404NotFound,
                success = false,
                message = "Account not found."
            });
        }

        if (owner.UserType != UserType.PropertyOwner)
        {
            await _accessLogService.LogAsync(
                User,
                "SuspendOwner",
                false,
                $"UserId={owner.UserId} is not a PropertyOwner");

            return BadRequest(new
            {
                code = StatusCodes.Status400BadRequest,
                success = false,
                message = "The specified account is not a property owner."
            });
        }

        if (string.Equals(
            owner.AccountStatus,
            SuspendedStatus,
            StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(new
            {
                code = StatusCodes.Status409Conflict,
                success = false,
                message = "This owner is already suspended."
            });
        }

        owner.AccountStatus = SuspendedStatus;
        owner.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        await _accessLogService.LogAsync(
            User,
            "SuspendOwner",
            true,
            $"Suspended UserId={owner.UserId}, Email={owner.Email}");

        _logger.LogInformation(
            "Owner suspended. UserId={UserId}, Email={Email}",
            owner.UserId,
            owner.Email);

        return Ok(new
        {
            code = StatusCodes.Status200OK,
            success = true,
            message = "Owner account suspended successfully.",
            data = new
            {
                owner.UserId,
                owner.Email,
                owner.FirstName,
                owner.LastName,
                owner.AccountStatus,
                owner.UpdatedAt
            }
        });
    }

    // POST /api/admin/owner-suspensions/reintegrate
    [HttpPost("reintegrate")]
    public async Task<IActionResult> ReintegrateOwner(
        [FromBody] OwnerSuspensionRequest request)
    {
        var normalizedEmail = request.Email.Trim().ToLower();

        var owner = await _context.Users
            .FirstOrDefaultAsync(u =>
                u.Email.ToLower() == normalizedEmail);

        if (owner == null)
        {
            await _accessLogService.LogAsync(
                User,
                "ReintegrateOwner",
                false,
                $"Owner not found: {request.Email}");

            return NotFound(new
            {
                code = StatusCodes.Status404NotFound,
                success = false,
                message = "Account not found."
            });
        }

        if (owner.UserType != UserType.PropertyOwner)
        {
            return BadRequest(new
            {
                code = StatusCodes.Status400BadRequest,
                success = false,
                message = "The specified account is not a property owner."
            });
        }

        if (!string.Equals(
            owner.AccountStatus,
            SuspendedStatus,
            StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(new
            {
                code = StatusCodes.Status409Conflict,
                success = false,
                message = "This owner is not currently suspended."
            });
        }

        owner.AccountStatus = ActiveStatus;
        owner.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        await _accessLogService.LogAsync(
            User,
            "ReintegrateOwner",
            true,
            $"Reintegrated UserId={owner.UserId}, Email={owner.Email}");

        _logger.LogInformation(
            "Owner reintegrated. UserId={UserId}, Email={Email}",
            owner.UserId,
            owner.Email);

        return Ok(new
        {
            code = StatusCodes.Status200OK,
            success = true,
            message = "Owner account reintegrated successfully.",
            data = new
            {
                owner.UserId,
                owner.Email,
                owner.FirstName,
                owner.LastName,
                owner.AccountStatus,
                owner.UpdatedAt
            }
        });
    }
}