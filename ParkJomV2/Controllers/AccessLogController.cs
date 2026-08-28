using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services;

namespace ParkJomV2.Controllers;

[ApiController]
[Route("api/accesslog")]
public class AccessLogController : ControllerBase
{
    private const int DefaultPageSize = 100;
    private const int MaxPageSize = 1000;

    private readonly ApplicationDbContext _context;
    private readonly CurrentUserService _currentUser;
    private readonly ILogger<AccessLogController> _logger;

    public AccessLogController(ApplicationDbContext context, CurrentUserService currentUser, ILogger<AccessLogController> logger)
    {
        _context = context;
        _currentUser = currentUser;
        _logger = logger;
    }

    /// <summary>
    /// Get access logs with optional paging, sorting, filtering and search. Admin only.
    /// By default all matching logs are returned. Supply page and/or pageSize to enable paging.
    /// pageSize defaults to 100 when paging is requested and has a maximum of 1000.
    /// Default: sorted by AccessedAt descending.
    /// sort   : desc (default) | asc  (always ordered by AccessedAt)
    /// type   : booking | user | iot
    ///   - booking : logs with a booking context (BookingId != null)
    ///   - user    : general user action logs (only UserId set)
    ///   - iot     : IoT device logs (IoTDeviceId != null)
    /// search : free-text match against the action description or the user's email.
    /// </summary>
    [Authorize]
    [HttpGet]
    [ProducesResponseType(typeof(AccessLogListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<AccessLogListResponse>> GetAccessLogs(
        [FromQuery] string? sort = null,
        [FromQuery] string? type = null,
        [FromQuery] string? search = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null)
    {
        try
        {
            var user = await _currentUser.GetCurrentUserAsync();

            if (user == null)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "User not found"
                });
            }

            if (user.UserType != UserType.Admin)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
                {
                    Code = StatusCodes.Status403Forbidden,
                    Success = false,
                    Message = "Only administrators can access access logs"
                });
            }

            // ---- Normalize type filter ----
            var normalizedType = string.IsNullOrWhiteSpace(type) ? null : type.Trim().ToLowerInvariant();
            if (normalizedType is not (null or "booking" or "user" or "iot"))
            {
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "type must be one of: booking, user, iot"
                });
            }

            // sort direction: default to desc if not specified, otherwise must be asc or desc
            var sortDirection = string.IsNullOrWhiteSpace(sort) ? "desc" : sort.Trim().ToLowerInvariant();
            if (sortDirection is not ("asc" or "desc"))
            {
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "sort must be 'asc' or 'desc'"
                });
            }

            var pagingRequested = page.HasValue || pageSize.HasValue;
            var appliedPage = page.GetValueOrDefault(1);
            var appliedPageSize = pageSize.GetValueOrDefault(DefaultPageSize);

            if (appliedPage < 1)
            {
                appliedPage = 1;
            }

            if (pagingRequested && (appliedPageSize < 1 || appliedPageSize > MaxPageSize))
            {
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = $"pageSize must be between 1 and {MaxPageSize}"
                });
            }

            var query = _context.AccessLogs.AsNoTracking().AsQueryable();

            // ---- Filter by log type ----
            switch (normalizedType)
            {
                case "booking":
                    query = query.Where(l => l.BookingId != null);
                    break;
                case "user":
                    query = query.Where(l => l.UserId != null && l.BookingId == null && l.IoTDeviceId == null);
                    break;
                case "iot":
                    query = query.Where(l => l.IoTDeviceId != null);
                    break;
            }

            // ---- Search ----
            var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
            if (!string.IsNullOrWhiteSpace(normalizedSearch))
            {
                var term = normalizedSearch.ToLowerInvariant();
                query = query.Where(l => l.Actions.ToLower().Contains(term)
                    || (l.User != null && l.User.Email.ToLower().Contains(term)));
            }

            var total = await query.CountAsync();
            var responsePageSize = pagingRequested ? appliedPageSize : total;
            var totalPages = pagingRequested
                ? (int)Math.Ceiling(total / (double)appliedPageSize)
                : total == 0 ? 0 : 1;

            // ---- Sort (always on AccessedAt) ----
            query = sortDirection == "asc"
                ? query.OrderBy(l => l.AccessedAt).ThenBy(l => l.AccessLogId)
                : query.OrderByDescending(l => l.AccessedAt).ThenByDescending(l => l.AccessLogId);

            if (pagingRequested)
            {
                query = query
                    .Skip((appliedPage - 1) * appliedPageSize)
                    .Take(appliedPageSize);
            }

            var logs = await query
                .Include(l => l.User)
                .ToListAsync();

            var result = logs.Select(MapToDTO).ToList();

            var appliedSort = sortDirection;

            _logger.LogInformation(
                "Retrieved {Count} access logs (page {Page}/{TotalPages}, sort '{Sort}', type '{Type}', search '{Search}') for admin user {UserId}",
                result.Count, appliedPage, totalPages, appliedSort, normalizedType, normalizedSearch, userId);

            return Ok(new AccessLogListResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Access logs retrieved successfully",
                Data = result,
                Page = appliedPage,
                PageSize = responsePageSize,
                Total = total,
                TotalPages = totalPages,
                Sort = appliedSort,
                Type = normalizedType,
                Search = normalizedSearch
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving access logs");
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while retrieving access logs"
            });
        }
    }

    private static AccessLogDTO MapToDTO(AccessLog log)
    {
        return new AccessLogDTO
        {
            AccessLogId = log.AccessLogId,
            BookingId = log.BookingId,
            UserId = log.UserId,
            IoTDeviceId = log.IoTDeviceId,
            Actions = log.Actions,
            AccessedAt = log.AccessedAt,
            CreatedAt = log.CreatedAt,
            UserEmail = log.User?.Email,
            UserName = $"{log.User?.FirstName} {log.User?.LastName}".Trim()
        };
    }
}
