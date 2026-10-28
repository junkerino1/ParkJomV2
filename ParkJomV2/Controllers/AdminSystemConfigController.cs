using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Services;
using System.Security.Claims;

namespace ParkJomV2.Controllers;

[ApiController]
[Route("api/admin/system-config")]
[Authorize(Policy = "AdminOnly")]
public class AdminSystemConfigController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly AccessLogService _accessLogService;
    private readonly ILogger<AdminSystemConfigController> _logger;

    public AdminSystemConfigController(
        ApplicationDbContext context,
        AccessLogService accessLogService,
        ILogger<AdminSystemConfigController> logger)
    {
        _context = context;
        _accessLogService = accessLogService;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/admin/system-config
    /// Returns active platform commission rate and overstay grace period.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(SystemConfigResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<SystemConfigResponse>> GetSystemConfig()
    {
        try
        {
            var config = await _context.SystemConfigurations
                .Include(c => c.UpdatedByUser)
                .FirstOrDefaultAsync(c => c.Id == 1);

            if (config == null)
            {
                config = new SystemConfiguration
                {
                    Id = 1,
                    CommissionRate = 10.00m,
                    GracePeriodMinutes = 15,
                    UpdatedAt = DateTime.UtcNow,
                    UpdatedByUserId = null
                };
                _context.SystemConfigurations.Add(config);
                await _context.SaveChangesAsync();
            }

            var dto = new SystemConfigDTO
            {
                CommissionRate = config.CommissionRate,
                GracePeriodMinutes = config.GracePeriodMinutes,
                UpdatedAt = config.UpdatedAt,
                UpdatedByUserId = config.UpdatedByUserId,
                UpdatedByName = config.UpdatedByUser != null
                    ? $"{config.UpdatedByUser.FirstName} {config.UpdatedByUser.LastName}".Trim()
                    : null
            };

            return Ok(new SystemConfigResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "System configuration retrieved successfully",
                Data = dto
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving system configuration");
            return StatusCode(StatusCodes.Status500InternalServerError, new SystemConfigResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while retrieving system configuration"
            });
        }
    }

    /// <summary>
    /// PUT /api/admin/system-config
    /// Updates the platform commission rate or grace period.
    /// </summary>
    [HttpPut]
    [ProducesResponseType(typeof(SystemConfigResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(SystemConfigResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SystemConfigResponse>> UpdateSystemConfig([FromBody] UpdateSystemConfigRequest request)
    {
        if (request.CommissionRate == null && request.GracePeriodMinutes == null)
        {
            return BadRequest(new SystemConfigResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Success = false,
                Message = "No configuration changes provided."
            });
        }

        if (request.CommissionRate.HasValue && (request.CommissionRate.Value < 0 || request.CommissionRate.Value > 100))
        {
            return BadRequest(new SystemConfigResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Success = false,
                Message = "Commission rate must be between 0% and 100%."
            });
        }

        if (request.GracePeriodMinutes.HasValue && (request.GracePeriodMinutes.Value < 0 || request.GracePeriodMinutes.Value > 1440))
        {
            return BadRequest(new SystemConfigResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Success = false,
                Message = "Grace period must be between 0 and 1440 minutes."
            });
        }

        try
        {
            var config = await _context.SystemConfigurations
                .Include(c => c.UpdatedByUser)
                .FirstOrDefaultAsync(c => c.Id == 1);

            if (config == null)
            {
                config = new SystemConfiguration
                {
                    Id = 1,
                    CommissionRate = 10.00m,
                    GracePeriodMinutes = 15
                };
                _context.SystemConfigurations.Add(config);
            }

            if (request.CommissionRate.HasValue)
            {
                config.CommissionRate = Math.Round(request.CommissionRate.Value, 2);
            }

            if (request.GracePeriodMinutes.HasValue)
            {
                config.GracePeriodMinutes = request.GracePeriodMinutes.Value;
            }

            config.UpdatedAt = DateTime.UtcNow;

            var userIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (int.TryParse(userIdText, out var adminUserId))
            {
                config.UpdatedByUserId = adminUserId;
            }

            await _context.SaveChangesAsync();

            // Reload UpdatedByUser navigation property
            await _context.Entry(config).Reference(c => c.UpdatedByUser).LoadAsync();

            await _accessLogService.LogAsync(
                User,
                "UpdateSystemConfig",
                true,
                $"CommissionRate={config.CommissionRate}%, GracePeriod={config.GracePeriodMinutes}m");

            var dto = new SystemConfigDTO
            {
                CommissionRate = config.CommissionRate,
                GracePeriodMinutes = config.GracePeriodMinutes,
                UpdatedAt = config.UpdatedAt,
                UpdatedByUserId = config.UpdatedByUserId,
                UpdatedByName = config.UpdatedByUser != null
                    ? $"{config.UpdatedByUser.FirstName} {config.UpdatedByUser.LastName}".Trim()
                    : null
            };

            return Ok(new SystemConfigResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "System configuration updated successfully",
                Data = dto
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating system configuration");
            return StatusCode(StatusCodes.Status500InternalServerError, new SystemConfigResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while updating system configuration"
            });
        }
    }
}
