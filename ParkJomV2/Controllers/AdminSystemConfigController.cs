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
                    OverstayPenaltyMultiplier = 1.50m,
                    OverstayMaxDailyPenalty = 30.00m,
                    HardwareExitLockEnabled = true,
                    TaxRatePercent = 8.00m,
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
                OverstayPenaltyMultiplier = config.OverstayPenaltyMultiplier,
                OverstayMaxDailyPenalty = config.OverstayMaxDailyPenalty,
                HardwareExitLockEnabled = config.HardwareExitLockEnabled,
                TaxRatePercent = config.TaxRatePercent,
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
        if (request.CommissionRate == null
            && request.GracePeriodMinutes == null
            && request.OverstayPenaltyMultiplier == null
            && request.OverstayMaxDailyPenalty == null
            && request.HardwareExitLockEnabled == null
            && request.TaxRatePercent == null)
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

        if (request.OverstayPenaltyMultiplier.HasValue && (request.OverstayPenaltyMultiplier.Value < 1.0m || request.OverstayPenaltyMultiplier.Value > 5.0m))
        {
            return BadRequest(new SystemConfigResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Success = false,
                Message = "Overstay penalty multiplier must be between 1.0x and 5.0x."
            });
        }

        if (request.OverstayMaxDailyPenalty.HasValue && (request.OverstayMaxDailyPenalty.Value < 0m || request.OverstayMaxDailyPenalty.Value > 500m))
        {
            return BadRequest(new SystemConfigResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Success = false,
                Message = "Maximum daily overstay penalty must be between RM 0 and RM 500."
            });
        }

        if (request.TaxRatePercent.HasValue && (request.TaxRatePercent.Value < 0m || request.TaxRatePercent.Value > 30m))
        {
            return BadRequest(new SystemConfigResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Success = false,
                Message = "Tax rate percent must be between 0% and 30%."
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
                    GracePeriodMinutes = 15,
                    OverstayPenaltyMultiplier = 1.50m,
                    OverstayMaxDailyPenalty = 30.00m,
                    HardwareExitLockEnabled = true,
                    TaxRatePercent = 8.00m
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

            if (request.OverstayPenaltyMultiplier.HasValue)
            {
                config.OverstayPenaltyMultiplier = Math.Round(request.OverstayPenaltyMultiplier.Value, 2);
            }

            if (request.OverstayMaxDailyPenalty.HasValue)
            {
                config.OverstayMaxDailyPenalty = Math.Round(request.OverstayMaxDailyPenalty.Value, 2);
            }

            if (request.HardwareExitLockEnabled.HasValue)
            {
                config.HardwareExitLockEnabled = request.HardwareExitLockEnabled.Value;
            }

            if (request.TaxRatePercent.HasValue)
            {
                config.TaxRatePercent = Math.Round(request.TaxRatePercent.Value, 2);
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
                $"CommissionRate={config.CommissionRate}%, GracePeriod={config.GracePeriodMinutes}m, Multiplier={config.OverstayPenaltyMultiplier}x, Tax={config.TaxRatePercent}%");

            var dto = new SystemConfigDTO
            {
                CommissionRate = config.CommissionRate,
                GracePeriodMinutes = config.GracePeriodMinutes,
                OverstayPenaltyMultiplier = config.OverstayPenaltyMultiplier,
                OverstayMaxDailyPenalty = config.OverstayMaxDailyPenalty,
                HardwareExitLockEnabled = config.HardwareExitLockEnabled,
                TaxRatePercent = config.TaxRatePercent,
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
