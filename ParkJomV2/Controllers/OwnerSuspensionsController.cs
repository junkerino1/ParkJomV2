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
[Route("api/admin/account-suspensions")]
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

    // GET /api/admin/account-suspensions
    [HttpGet]
    public async Task<IActionResult> GetSuspendedOwners()
    {
        var suspendedAccounts = await _context.Users
            .AsNoTracking()
            .Where(u =>
                (u.UserType == UserType.PropertyOwner || u.UserType == UserType.Renter) &&
                u.AccountStatus == SuspendedStatus)
            .OrderByDescending(u => u.UpdatedAt)
            .Select(u => new SuspendedOwnerDTO
            {
                UserId = u.UserId,
                Email = u.Email,
                FirstName = u.FirstName,
                LastName = u.LastName,
                AccountStatus = u.AccountStatus,
                UserType = u.UserType,
                SuspendedParkingSpotCount = u.OwnedParkingSpots.Count(
                    p => p.AvailabilityStatus == AvailabilityStatus.Suspended),
                UpdatedAt = u.UpdatedAt
            })
            .ToListAsync();

        return Ok(new
        {
            code = StatusCodes.Status200OK,
            success = true,
            message = "Suspended accounts retrieved successfully.",
            data = suspendedAccounts
        });
    }

    // POST /api/admin/account-suspensions/suspend
    [HttpPost("suspend")]
    public async Task<IActionResult> SuspendOwner(
        [FromBody] OwnerSuspensionRequest request)
    {
        var normalizedEmail = request.Email.Trim().ToLower();

        var account = await _context.Users
            .FirstOrDefaultAsync(u =>
                u.Email.ToLower() == normalizedEmail);

        if (account == null)
        {
            await _accessLogService.LogAsync(
                User,
                "SuspendAccount",
                false,
                $"Account not found: {request.Email}");

            return NotFound(new
            {
                code = StatusCodes.Status404NotFound,
                success = false,
                message = "Account not found."
            });
        }

        if (account.UserType == UserType.Admin)
        {
            await _accessLogService.LogAsync(
                User,
                "SuspendAccount",
                false,
                $"Attempted to suspend admin UserId={account.UserId}");

            return BadRequest(new
            {
                code = StatusCodes.Status400BadRequest,
                success = false,
                message = "Admin accounts cannot be suspended through this endpoint."
            });
        }

        if (string.Equals(
            account.AccountStatus,
            SuspendedStatus,
            StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(new
            {
                code = StatusCodes.Status409Conflict,
                success = false,
                message = "This account is already suspended."
            });
        }

        account.AccountStatus = SuspendedStatus;
        account.UpdatedAt = DateTime.UtcNow;

        var suspendedParkingSpots = new List<Models.ParkingSpot>();
        if (account.UserType == UserType.PropertyOwner)
        {
            suspendedParkingSpots = await _context.ParkingSpots
                .Where(p =>
                    p.OwnerId == account.UserId &&
                    p.AvailabilityStatus == AvailabilityStatus.Available)
                .ToListAsync();

            foreach (var parkingSpot in suspendedParkingSpots)
            {
                parkingSpot.AvailabilityStatus = AvailabilityStatus.Suspended;
                parkingSpot.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _context.SaveChangesAsync();

        await _accessLogService.LogAsync(
            User,
            "SuspendAccount",
            true,
            $"Suspended UserId={account.UserId}, Email={account.Email}, SuspendedParkingSpots={suspendedParkingSpots.Count}");

        _logger.LogInformation(
            "Account suspended. UserId={UserId}, Email={Email}, UserType={UserType}, SuspendedParkingSpots={SuspendedParkingSpots}",
            account.UserId,
            account.Email,
            account.UserType,
            suspendedParkingSpots.Count);

        return Ok(new
        {
            code = StatusCodes.Status200OK,
            success = true,
            message = "Account suspended successfully.",
            data = new
            {
                account.UserId,
                account.Email,
                account.FirstName,
                account.LastName,
                account.UserType,
                account.AccountStatus,
                SuspendedParkingSpotCount = suspendedParkingSpots.Count,
                account.UpdatedAt
            }
        });
    }

    // POST /api/admin/account-suspensions/reintegrate
    [HttpPost("reintegrate")]
    public async Task<IActionResult> ReintegrateOwner(
        [FromBody] OwnerSuspensionRequest request)
    {
        var normalizedEmail = request.Email.Trim().ToLower();

        var account = await _context.Users
            .FirstOrDefaultAsync(u =>
                u.Email.ToLower() == normalizedEmail);

        if (account == null)
        {
            await _accessLogService.LogAsync(
                User,
                "ReintegrateAccount",
                false,
                $"Account not found: {request.Email}");

            return NotFound(new
            {
                code = StatusCodes.Status404NotFound,
                success = false,
                message = "Account not found."
            });
        }

        if (account.UserType == UserType.Admin)
        {
            return BadRequest(new
            {
                code = StatusCodes.Status400BadRequest,
                success = false,
                message = "Admin accounts cannot be reintegrated through this endpoint."
            });
        }

        if (!string.Equals(
            account.AccountStatus,
            SuspendedStatus,
            StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(new
            {
                code = StatusCodes.Status409Conflict,
                success = false,
                message = "This account is not currently suspended."
            });
        }

        account.AccountStatus = ActiveStatus;
        account.UpdatedAt = DateTime.UtcNow;

        var reinstatedParkingSpots = new List<Models.ParkingSpot>();
        if (account.UserType == UserType.PropertyOwner)
        {
            reinstatedParkingSpots = await _context.ParkingSpots
                .Where(p =>
                    p.OwnerId == account.UserId &&
                    p.AvailabilityStatus == AvailabilityStatus.Suspended)
                .ToListAsync();

            foreach (var parkingSpot in reinstatedParkingSpots)
            {
                parkingSpot.AvailabilityStatus = AvailabilityStatus.Available;
                parkingSpot.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _context.SaveChangesAsync();

        await _accessLogService.LogAsync(
            User,
            "ReintegrateAccount",
            true,
            $"Reintegrated UserId={account.UserId}, Email={account.Email}, ReinstatedParkingSpots={reinstatedParkingSpots.Count}");

        _logger.LogInformation(
            "Account reintegrated. UserId={UserId}, Email={Email}, UserType={UserType}, ReinstatedParkingSpots={ReinstatedParkingSpots}",
            account.UserId,
            account.Email,
            account.UserType,
            reinstatedParkingSpots.Count);

        return Ok(new
        {
            code = StatusCodes.Status200OK,
            success = true,
            message = "Account reintegrated successfully.",
            data = new
            {
                account.UserId,
                account.Email,
                account.FirstName,
                account.LastName,
                account.UserType,
                account.AccountStatus,
                ReinstatedParkingSpotCount = reinstatedParkingSpots.Count,
                account.UpdatedAt
            }
        });
    }
}
