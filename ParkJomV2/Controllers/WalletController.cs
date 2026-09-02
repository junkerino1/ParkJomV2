using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services;
using System.ComponentModel.DataAnnotations;

namespace ParkJomV2.Controllers;

[ApiController]
[Authorize]
[Route("api/wallet")]
public class WalletController : ControllerBase
{
	private readonly ApplicationDbContext _context;
	private readonly StripeService _stripeService;
	private readonly AccessLogService _accessLogService;
	private readonly CurrentUserService _currentUser;
	private readonly ILogger<WalletController> _logger;

	public WalletController(
		ApplicationDbContext context,
		StripeService stripeService,
		AccessLogService accessLogService,
		CurrentUserService currentUser,
		ILogger<WalletController> logger)
	{
		_context = context;
		_stripeService = stripeService;
		_accessLogService = accessLogService;
		_currentUser = currentUser;
		_logger = logger;
	}

	/// <summary>
	/// Returns the authenticated user's wallet summary (balance, on-hold, etc.).
	/// </summary>
	[HttpGet]
	[ProducesResponseType(typeof(WalletSummaryResponse), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
	[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
	[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
	public async Task<ActionResult<WalletSummaryResponse>> GetWallet(CancellationToken cancellationToken)
	{
		var userId = _currentUser.UserId;
		if (!userId.HasValue)
		{
			await _accessLogService.LogAsync(User, "GetWallet", false, "Authentication required");
			return Unauthorized(CreateError(StatusCodes.Status401Unauthorized, "Authentication required."));
		}

		try
		{
			var summary = await _stripeService.GetWalletAsync(userId.Value, cancellationToken);
			await _accessLogService.LogAsync(User, "GetWallet", true, $"WalletId={summary.WalletId}");
			return Ok(summary);
		}
		catch (KeyNotFoundException ex)
		{
			await _accessLogService.LogAsync(User, "GetWallet", false, ex.Message);
			return NotFound(CreateError(StatusCodes.Status404NotFound, ex.Message));
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error retrieving wallet for user {UserId}", userId.Value);
			await _accessLogService.LogAsync(User, "GetWallet", false, ex.Message);
			return StatusCode(
				StatusCodes.Status500InternalServerError,
				CreateError(StatusCodes.Status500InternalServerError, "An error occurred while retrieving the wallet."));
		}
	}

	/// <summary>
	/// Returns the authenticated user's wallet transaction history (ledger), newest first.
	/// Returns the authenticated user's wallet transaction history (ledger), newest first.
	/// Optional filters: month (YYYY-MM) on the transaction date, type
	/// (TopUp, Payment, Refund, Withdrawal, OwnerPayout, PlatformCommission, OverstayPenalty),
	/// status (Pending, Completed, Failed, Refunded), plus pagination.
	/// </summary>
	[HttpGet("history")]
	[ProducesResponseType(typeof(WalletHistoryResponse), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
	[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
	[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
	public async Task<ActionResult<WalletHistoryResponse>> GetWalletHistory(
		[FromQuery] string? month = null,
		[FromQuery] string? type = null,
		[FromQuery] string? status = null,
		[FromQuery, Range(1, 1_000_000)] int page = 1,
		[FromQuery, Range(1, 50)] int pageSize = 10)
	{
		var userId = _currentUser.UserId;
		if (!userId.HasValue)
		{
			await _accessLogService.LogAsync(User, "GetWalletHistory", false, "Authentication required");
			return Unauthorized(CreateError(StatusCodes.Status401Unauthorized, "Authentication required."));
		}

		DateOnly? monthStart = null;
		DateOnly? monthEndExclusive = null;
		if (!string.IsNullOrWhiteSpace(month))
		{
			if (!CalendarMonthParser.TryParse(month, out var parsedStart, out var parsedEndExclusive))
			{
				return BadRequest(CreateError(
					StatusCodes.Status400BadRequest,
					"month must use YYYY-MM format."));
			}

			monthStart = parsedStart;
			monthEndExclusive = parsedEndExclusive;
		}

		if (!TryParseTransactionStatus(status, out var transactionStatus))
		{
			return BadRequest(CreateError(
				StatusCodes.Status400BadRequest,
				"status must be one of: Pending, Completed, Failed, Refunded."));
		}

		if (!TryParseTransactionType(type, out var transactionType))
		{
			return BadRequest(CreateError(
				StatusCodes.Status400BadRequest,
				"type must be one of: TopUp, Payment, Refund, Withdrawal, OwnerPayout, PlatformCommission, OverstayPenalty."));
		}

		try
		{
			var wallet = await _context.Wallets
				.AsNoTracking()
				.FirstOrDefaultAsync(w => w.UserId == userId.Value);

			if (wallet == null)
			{
				await _accessLogService.LogAsync(User, "GetWalletHistory", true, "No wallet found");
				return Ok(new WalletHistoryResponse
				{
					Code = StatusCodes.Status200OK,
					Success = true,
					Message = "No wallet history found.",
					Month = monthStart?.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture),
					Type = transactionType?.ToString(),
					Status = transactionStatus?.ToString(),
					TotalCount = 0,
					Page = page,
					PageSize = pageSize,
					TotalPages = 0,
					Data = new List<WalletHistoryItemResponse>()
				});
			}

			var query = _context.Transactions
				.AsNoTracking()
				.Where(t => t.WalletId == wallet.WalletId);

			if (monthStart.HasValue && monthEndExclusive.HasValue)
			{
				var startBoundary = monthStart.Value.ToDateTime(TimeOnly.MinValue);
				var endExclusiveBoundary = monthEndExclusive.Value.ToDateTime(TimeOnly.MinValue);
				query = query.Where(t => t.CreatedAt >= startBoundary && t.CreatedAt < endExclusiveBoundary);
			}

			if (transactionStatus.HasValue)
			{
				query = query.Where(t => t.TransactionStatus == transactionStatus.Value);
			}

			if (transactionType.HasValue)
			{
				query = query.Where(t => t.TransactionType == transactionType.Value);
			}

			var totalCount = await query.CountAsync();
			var transactions = await query
				.OrderByDescending(t => t.CreatedAt)
				.ThenByDescending(t => t.TransactionId)
				.Skip((page - 1) * pageSize)
				.Take(pageSize)
				.ToListAsync();

			await _accessLogService.LogAsync(
				User,
				"GetWalletHistory",
				true,
				$"Month={monthStart?.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture) ?? "all"}; " +
				$"Type={transactionType?.ToString() ?? "all"}; Status={transactionStatus?.ToString() ?? "all"}; " +
				$"Page={page}; PageSize={pageSize}; Returned={transactions.Count}; Total={totalCount}");

			return Ok(new WalletHistoryResponse
			{
				Code = StatusCodes.Status200OK,
				Success = true,
				Message = transactions.Count > 0
					? "Wallet history retrieved successfully."
					: "No wallet history found.",
				Month = monthStart?.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture),
				Type = transactionType?.ToString(),
				Status = transactionStatus?.ToString(),
				TotalCount = totalCount,
				Page = page,
				PageSize = pageSize,
				TotalPages = totalCount == 0
					? 0
					: (int)Math.Ceiling(totalCount / (double)pageSize),
				Data = transactions.Select(MapHistoryItem).ToList()
			});
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error retrieving wallet history for user {UserId}", userId.Value);
			await _accessLogService.LogAsync(User, "GetWalletHistory", false, ex.Message);
			return StatusCode(
				StatusCodes.Status500InternalServerError,
				CreateError(StatusCodes.Status500InternalServerError, "An error occurred while retrieving the wallet history."));
		}
	}

	private static bool TryParseTransactionType(string? type, out TransactionType? transactionType)
	{
		transactionType = null;
		if (string.IsNullOrWhiteSpace(type))
		{
			return true;
		}

		var match = Enum.GetNames<TransactionType>()
			.FirstOrDefault(candidate => string.Equals(
				candidate,
				type.Trim(),
				StringComparison.OrdinalIgnoreCase));

		if (match == null)
		{
			return false;
		}

		transactionType = Enum.Parse<TransactionType>(match);
		return true;
	}

	private static bool TryParseTransactionStatus(string? status, out TransactionStatus? transactionStatus)
	{
		transactionStatus = null;
		if (string.IsNullOrWhiteSpace(status))
		{
			return true;
		}

		var match = Enum.GetNames<TransactionStatus>()
			.FirstOrDefault(candidate => string.Equals(
				candidate,
				status.Trim(),
				StringComparison.OrdinalIgnoreCase));

		if (match == null)
		{
			return false;
		}

		transactionStatus = Enum.Parse<TransactionStatus>(match);
		return true;
	}

	private static WalletHistoryItemResponse MapHistoryItem(Transaction transaction)
	{
		return new WalletHistoryItemResponse
		{
			TransactionId = transaction.TransactionId,
			Type = transaction.TransactionType.ToString(),
			Amount = transaction.Amount,
			Status = transaction.TransactionStatus.ToString(),
			PaymentMethod = transaction.PaymentMethod.ToString(),
			ReferenceNumber = transaction.ReferenceNumber,
			BookingId = transaction.BookingId,
			CreatedAt = transaction.CreatedAt
		};
	}

	private static ErrorResponse CreateError(int code, string message)
	{
		return new ErrorResponse
		{
			Code = code,
			Success = false,
			Message = message
		};
	}
}
