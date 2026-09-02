using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services;
using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace ParkJomV2.Controllers;

// =====================================================================
// Owner Earnings API — base route: /api/owner/earnings (requires owner)
//
//   GET /api/owner/earnings/summary?month=YYYY-MM
//       All-time earnings summary: total = available + held + paid,
//       plus booking counts and gross. Optional month returns MonthlyEarnings.
//       Sample:  GET /api/owner/earnings/summary
//                GET /api/owner/earnings/summary?month=2026-09
//
//   GET /api/owner/earnings/transactions?type=&month=&page=&pageSize=
//       Per-booking earnings feed: payout, platform commission (fee),
//       cancellation refunds, and overstay penalties.
//       type = payout | commission | cancellation | penalty (optional)
//       Sample:  GET /api/owner/earnings/transactions
//                GET /api/owner/earnings/transactions?type=cancellation&month=2026-09
// =====================================================================

[ApiController]
[Authorize]
[Route("api/owner/earnings")]
public class OwnerEarningsController : ControllerBase
{
    private const int MaximumPageSize = 50;
    private readonly ApplicationDbContext _context;
    private readonly CurrentUserService _currentUser;
    private readonly AccessLogService _accessLogService;
    private readonly ILogger<OwnerEarningsController> _logger;

    public OwnerEarningsController(
        ApplicationDbContext context,
        CurrentUserService currentUser,
        AccessLogService accessLogService,
        ILogger<OwnerEarningsController> logger)
    {
        _context = context;
        _currentUser = currentUser;
        _accessLogService = accessLogService;
        _logger = logger;
    }

    /// <summary>
    /// Returns the authenticated owner's all-time earnings summary.
    /// Total = available (wallet balance) + held (on-hold payout) + paid (withdrawn).
    /// An optional month (YYYY-MM) adds MonthlyEarnings for bookings starting that month.
    /// </summary>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(OwnerEarningsSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<OwnerEarningsSummaryResponse>> GetEarningsSummary([FromQuery] string? month = null)
    {
        DateOnly? monthStart = null;
        DateOnly? monthEndExclusive = null;
        if (!string.IsNullOrWhiteSpace(month))
        {
            if (!CalendarMonthParser.TryParse(month, out var parsedStart, out var parsedEndExclusive))
            {
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "month must use YYYY-MM format."
                });
            }

            monthStart = parsedStart;
            monthEndExclusive = parsedEndExclusive;
        }

        var owner = await _currentUser.GetCurrentUserAsync();
        if (owner == null || owner.UserType != UserType.PropertyOwner)
        {
            await _accessLogService.LogAsync(User, "GetOwnerEarningsSummary", false, "Only parking owners can view earnings");
            return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
            {
                Code = StatusCodes.Status403Forbidden,
                Success = false,
                Message = "Only parking owners can view earnings."
            });
        }

        try
        {
            var userId = owner.UserId;
            var wallet = await _context.Wallets
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.UserId == userId);

            var availableEarnings = wallet?.Balance ?? 0m;
            var heldEarnings = wallet?.OnHold ?? 0m;
            var paidEarnings = 0m;
            if (wallet != null)
            {
                var withdrawn = await _context.Transactions
                    .AsNoTracking()
                    .Where(t => t.WalletId == wallet.WalletId && t.TransactionType == TransactionType.Withdrawal)
                    .SumAsync(t => (decimal?)t.Amount) ?? 0m;
                paidEarnings = Math.Abs(withdrawn);
            }

            var totalEarnings = availableEarnings + heldEarnings + paidEarnings;

            var bookingsQuery = _context.Bookings
                .AsNoTracking()
                .Where(b => b.ParkingSpot.OwnerId == userId);

            var totalBookings = await bookingsQuery.CountAsync();

            var statusCounts = new BookingStatusCountsResponse();
            var grouped = await bookingsQuery
                .GroupBy(b => b.BookingStatus)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();
            foreach (var entry in grouped)
            {
                switch (entry.Status)
                {
                    case BookingStatus.Pending: statusCounts.Pending = entry.Count; break;
                    case BookingStatus.Confirmed: statusCounts.Confirmed = entry.Count; break;
                    case BookingStatus.Active: statusCounts.Active = entry.Count; break;
                    case BookingStatus.Completed: statusCounts.Completed = entry.Count; break;
                    case BookingStatus.Cancelled: statusCounts.Cancelled = entry.Count; break;
                    case BookingStatus.Expired: statusCounts.Expired = entry.Count; break;
                }
            }

            var earnedStatuses = new[] { BookingStatus.Confirmed, BookingStatus.Active, BookingStatus.Completed };
            var grossEarnings = await bookingsQuery
                .Where(b => earnedStatuses.Contains(b.BookingStatus))
                .SumAsync(b => (decimal?)b.OwnerPayoutAmount) ?? 0m;

            decimal? monthlyEarnings = null;
            if (monthStart.HasValue && monthEndExclusive.HasValue)
            {
                var startBoundary = monthStart.Value.ToDateTime(TimeOnly.MinValue);
                var endExclusiveBoundary = monthEndExclusive.Value.ToDateTime(TimeOnly.MinValue);
                monthlyEarnings = await bookingsQuery
                    .Where(b => earnedStatuses.Contains(b.BookingStatus))
                    .Where(b => b.StartDate >= startBoundary && b.StartDate < endExclusiveBoundary)
                    .SumAsync(b => (decimal?)b.OwnerPayoutAmount) ?? 0m;
            }

            await _accessLogService.LogAsync(
                User,
                "GetOwnerEarningsSummary",
                true,
                $"Month={monthStart?.ToString("yyyy-MM", CultureInfo.InvariantCulture) ?? "all"}; Total={totalEarnings}");

            return Ok(new OwnerEarningsSummaryResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = "Owner earnings summary retrieved successfully.",
                Month = monthStart?.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                TotalEarnings = totalEarnings,
                AvailableEarnings = availableEarnings,
                HeldEarnings = heldEarnings,
                PaidEarnings = paidEarnings,
                MonthlyEarnings = monthlyEarnings,
                TotalBookings = totalBookings,
                GrossEarnings = grossEarnings,
                BookingCounts = statusCounts
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving owner earnings summary for user {UserId}", owner.UserId);
            await _accessLogService.LogAsync(User, "GetOwnerEarningsSummary", false, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while retrieving the earnings summary."
            });
        }
    }

    /// <summary>
    /// Returns a paged, per-booking earnings feed for the authenticated owner's parking spots.
    /// Optional type filter: payout, commission, cancellation, or penalty.
    /// Optional month (YYYY-MM) filters by booking start date.
    /// </summary>
    [HttpGet("transactions")]
    [ProducesResponseType(typeof(OwnerEarningsTransactionListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<OwnerEarningsTransactionListResponse>> GetEarningsTransactions(
        [FromQuery] string? type = null,
        [FromQuery] string? month = null,
        [FromQuery, Range(1, 1_000_000)] int page = 1,
        [FromQuery, Range(1, MaximumPageSize)] int pageSize = 10)
    {
        if (!TryParseType(type, out var normalizedType))
        {
            return BadRequest(new ErrorResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Success = false,
                Message = "type must be one of: payout, commission, cancellation, penalty."
            });
        }

        DateOnly? monthStart = null;
        DateOnly? monthEndExclusive = null;
        if (!string.IsNullOrWhiteSpace(month))
        {
            if (!CalendarMonthParser.TryParse(month, out var parsedStart, out var parsedEndExclusive))
            {
                return BadRequest(new ErrorResponse
                {
                    Code = StatusCodes.Status400BadRequest,
                    Success = false,
                    Message = "month must use YYYY-MM format."
                });
            }

            monthStart = parsedStart;
            monthEndExclusive = parsedEndExclusive;
        }

        var owner = await _currentUser.GetCurrentUserAsync();
        if (owner == null || owner.UserType != UserType.PropertyOwner)
        {
            await _accessLogService.LogAsync(User, "GetOwnerEarningsTransactions", false, "Only parking owners can view earnings");
            return StatusCode(StatusCodes.Status403Forbidden, new ErrorResponse
            {
                Code = StatusCodes.Status403Forbidden,
                Success = false,
                Message = "Only parking owners can view earnings."
            });
        }

        try
        {
            var earnedStatuses = new[] { BookingStatus.Confirmed, BookingStatus.Active, BookingStatus.Completed };

            IQueryable<Booking> query = _context.Bookings
                .AsNoTracking()
                .Where(b => b.ParkingSpot.OwnerId == owner.UserId);

            if (monthStart.HasValue && monthEndExclusive.HasValue)
            {
                var startBoundary = monthStart.Value.ToDateTime(TimeOnly.MinValue);
                var endExclusiveBoundary = monthEndExclusive.Value.ToDateTime(TimeOnly.MinValue);
                query = query.Where(b => b.StartDate >= startBoundary && b.StartDate < endExclusiveBoundary);
            }

            if (!string.IsNullOrEmpty(normalizedType))
            {
                query = normalizedType switch
                {
                    "payout" => query.Where(b => earnedStatuses.Contains(b.BookingStatus)),
                    "commission" => query.Where(b => earnedStatuses.Contains(b.BookingStatus) && b.PlatformCommissionAmount > 0),
                    "cancellation" => query.Where(b => b.BookingStatus == BookingStatus.Cancelled),
                    "penalty" => query.Where(b => b.OverstayPenaltyAmount > 0),
                    _ => query
                };
            }

            var totalCount = await query.CountAsync();

            var bookings = await query
                .Include(b => b.ParkingSpot)
                .OrderByDescending(b => b.StartDate)
                .ThenByDescending(b => b.BookingId)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var data = bookings.Select(MapTransaction).ToList();

            await _accessLogService.LogAsync(
                User,
                "GetOwnerEarningsTransactions",
                true,
                $"Type={normalizedType ?? "all"}; Month={monthStart?.ToString("yyyy-MM", CultureInfo.InvariantCulture) ?? "all"}; " +
                $"Page={page}; PageSize={pageSize}; Returned={data.Count}; Total={totalCount}");

            return Ok(new OwnerEarningsTransactionListResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = data.Count > 0
                    ? "Earnings transactions retrieved successfully."
                    : "No earnings transactions found.",
                Type = normalizedType,
                Month = monthStart?.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = totalCount == 0
                    ? 0
                    : (int)Math.Ceiling(totalCount / (double)pageSize),
                Data = data
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving owner earnings transactions for user {UserId}", owner.UserId);
            await _accessLogService.LogAsync(User, "GetOwnerEarningsTransactions", false, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while retrieving the earnings transactions."
            });
        }
    }

    /// <summary>
    /// Parses and normalizes the optional earnings type filter.
    /// </summary>
    private static bool TryParseType(string? type, out string? normalizedType)
    {
        normalizedType = null;
        if (string.IsNullOrWhiteSpace(type))
        {
            return true;
        }

        var trimmed = type.Trim().ToLowerInvariant();
        if (trimmed is not ("payout" or "commission" or "cancellation" or "penalty"))
        {
            return false;
        }

        normalizedType = trimmed;
        return true;
    }

    /// <summary>
    /// Maps one owner booking into an earnings transaction row.
    /// A cancelled booking shows its released owner payout as a cancellation instead of a payout.
    /// </summary>
    private static OwnerEarningsTransactionResponse MapTransaction(Booking booking)
    {
        var isCancelled = booking.BookingStatus == BookingStatus.Cancelled;
        var payout = isCancelled ? 0m : booking.OwnerPayoutAmount;
        var commission = isCancelled ? 0m : booking.PlatformCommissionAmount;
        var cancellation = isCancelled ? booking.OwnerPayoutAmount : 0m;

        return new OwnerEarningsTransactionResponse
        {
            BookingId = booking.BookingId,
            BookingReference = booking.BookingReference,
            ParkingSpotId = booking.ParkingSpotId,
            ParkingLabel = booking.ParkingSpot?.ParkingLabel,
            StartDate = booking.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            EndDate = booking.EndDate.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            BookingStatus = booking.BookingStatus.ToString(),
            Payout = payout,
            Commission = commission,
            Cancellation = cancellation,
            Penalty = booking.OverstayPenaltyAmount,
            CreatedAt = booking.CreatedAt
        };
    }
}
