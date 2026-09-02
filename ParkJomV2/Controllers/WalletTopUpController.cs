using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services;
using System.ComponentModel.DataAnnotations;

namespace ParkJomV2.Controllers;

[ApiController]
[Route("api/wallet")]
public class WalletTopUpController : ControllerBase
{
	private readonly ApplicationDbContext _context;
	private readonly StripeService _stripeService;
	private readonly AccessLogService _accessLogService;
	private readonly CurrentUserService _currentUser;
	private readonly IConfiguration _configuration;
	private readonly ILogger<WalletTopUpController> _logger;

	public WalletTopUpController(
		ApplicationDbContext context,
		StripeService stripeService,
		AccessLogService accessLogService,
		CurrentUserService currentUser,
		IConfiguration configuration,
		ILogger<WalletTopUpController> logger)
	{
		_context = context;
		_stripeService = stripeService;
		_accessLogService = accessLogService;
		_currentUser = currentUser;
		_configuration = configuration;
		_logger = logger;
	}

	[Authorize]
	[HttpPost("topup")]
	[ProducesResponseType(typeof(WalletTopUpResponse), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
	[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
	[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
	public async Task<ActionResult<WalletTopUpResponse>> TopUp([FromBody] StripeTopUpRequest request)
	{
		if (!ModelState.IsValid)
		{
			await _accessLogService.LogAsync(User, "TopUp", false, "Invalid request");
			return BadRequest(new ErrorResponse
			{
				Code = StatusCodes.Status400BadRequest,
				Success = false,
				Message = "Invalid request."
			});
		}

		try
		{
			if (!TryGetUserId(out var userId))
			{
				await _accessLogService.LogAsync(User, "TopUp", false, "Authentication required");
				return Unauthorized(CreateError(StatusCodes.Status401Unauthorized, "Authentication required."));
			}

			var result = await _stripeService.CreateTopUpSessionAsync(userId, request);

			await _accessLogService.LogAsync(User, "TopUp", true, $"PaymentId={result.PaymentId}");
			return Ok(new WalletTopUpResponse
			{
				Code = StatusCodes.Status200OK,
				Success = true,
				Message = "Wallet top-up session created successfully.",
				PaymentId = result.PaymentId,
				SessionId = result.SessionId,
				CheckoutUrl = result.CheckoutUrl
			});
		}
		catch (InvalidOperationException ex)
		{
			await _accessLogService.LogAsync(User, "TopUp", false, ex.Message);
			return BadRequest(new ErrorResponse
			{
				Code = StatusCodes.Status400BadRequest,
				Success = false,
				Message = ex.Message
			});
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error creating wallet top-up session");
			await _accessLogService.LogAsync(User, "TopUp", false, ex.Message);
			return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
			{
				Code = StatusCodes.Status500InternalServerError,
				Success = false,
				Message = "An error occurred while creating the wallet top-up session."
			});
		}
	}

	/// <summary>
	/// Returns the authenticated user's wallet top-up history (Payments), newest first.
	/// Optional status filter (Pending, Completed, Cancelled, Failed) and pagination.
	/// </summary>
	[Authorize]
	[HttpGet("topup/history")]
	[ProducesResponseType(typeof(WalletTopUpHistoryResponse), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
	[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
	[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
	public async Task<ActionResult<WalletTopUpHistoryResponse>> GetTopUpHistory(
		[FromQuery] string? status = null,
		[FromQuery, Range(1, 1_000_000)] int page = 1,
		[FromQuery, Range(1, 50)] int pageSize = 10)
	{
		if (!TryGetUserId(out var userId))
		{
			await _accessLogService.LogAsync(User, "GetTopUpHistory", false, "Authentication required");
			return Unauthorized(CreateError(StatusCodes.Status401Unauthorized, "Authentication required."));
		}

		if (!TryParsePaymentStatus(status, out var paymentStatus))
		{
			await _accessLogService.LogAsync(User, "GetTopUpHistory", false, "Invalid status filter");
			return BadRequest(CreateError(
				StatusCodes.Status400BadRequest,
				"status must be one of: Pending, Completed, Cancelled, Failed."));
		}

		try
		{
			var query = _context.Payments
				.AsNoTracking()
				.Where(p => p.UserId == userId);

			if (paymentStatus.HasValue)
			{
				query = query.Where(p => p.Status == paymentStatus.Value);
			}

			var totalCount = await query.CountAsync();
			var payments = await query
				.OrderByDescending(p => p.CreatedAt)
				.ThenByDescending(p => p.PaymentId)
				.Skip((page - 1) * pageSize)
				.Take(pageSize)
				.ToListAsync();

			await _accessLogService.LogAsync(
				User,
				"GetTopUpHistory",
				true,
				$"Status={paymentStatus?.ToString() ?? "all"}; Page={page}; PageSize={pageSize}; " +
				$"Returned={payments.Count}; Total={totalCount}");

			return Ok(new WalletTopUpHistoryResponse
			{
				Code = StatusCodes.Status200OK,
				Success = true,
				Message = payments.Count > 0
					? "Top-up history retrieved successfully."
					: "No top-up history found.",
				TotalCount = totalCount,
				Page = page,
				PageSize = pageSize,
				TotalPages = totalCount == 0
					? 0
					: (int)Math.Ceiling(totalCount / (double)pageSize),
				Data = payments.Select(MapTopUpItem).ToList()
			});
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error retrieving top-up history for user {UserId}", userId);
			await _accessLogService.LogAsync(User, "GetTopUpHistory", false, ex.Message);
			return StatusCode(
				StatusCodes.Status500InternalServerError,
				CreateError(StatusCodes.Status500InternalServerError, "An error occurred while retrieving the top-up history."));
		}
	}


	// frontend poll status of the topup, if success, then show success message, if failed, then show failed message
	[Authorize]
	[HttpGet("topup/status")]
	[ProducesResponseType(typeof(WalletTopUpStatusResponse), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
	[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
	[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
	[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
	public async Task<ActionResult<WalletTopUpStatusResponse>> GetTopUpStatus(
		[FromQuery] string? sessionId,
		CancellationToken cancellationToken)
	{
		if (!TryGetUserId(out var userId))
		{
			await _accessLogService.LogAsync(User, "GetTopUpStatus", false, "Authentication required");
			return Unauthorized(CreateError(StatusCodes.Status401Unauthorized, "Authentication required."));
		}

		if (string.IsNullOrWhiteSpace(sessionId))
		{
			await _accessLogService.LogAsync(User, "GetTopUpStatus", false, "Session ID required");
			return BadRequest(CreateError(
				StatusCodes.Status400BadRequest,
				"A Stripe checkout session ID is required."));
		}

		try
		{
			var status = await _stripeService.GetTopUpStatusAsync(
				userId,
				sessionId,
				cancellationToken);
			await _accessLogService.LogAsync(User, "GetTopUpStatus", true, $"state={status.State}");
			return Ok(status);
		}
		catch (KeyNotFoundException ex)
		{
			await _accessLogService.LogAsync(User, "GetTopUpStatus", false, ex.Message);
			return NotFound(CreateError(StatusCodes.Status404NotFound, ex.Message));
		}
		catch (InvalidOperationException ex)
		{
			await _accessLogService.LogAsync(User, "GetTopUpStatus", false, ex.Message);
			return BadRequest(CreateError(StatusCodes.Status400BadRequest, ex.Message));
		}
		catch (Exception ex)
		{
			_logger.LogError(
				ex,
				"Error retrieving wallet top-up status for user {UserId}, session {SessionId}",
				userId,
				sessionId);
			await _accessLogService.LogAsync(User, "GetTopUpStatus", false, ex.Message);
			return StatusCode(
				StatusCodes.Status500InternalServerError,
				CreateError(StatusCodes.Status500InternalServerError, "An error occurred while checking the wallet top-up."));
		}
	}

	/// <summary>
	/// Stripe redirects the browser here after a successful payment.
	/// Navigation only - the wallet is credited by the webhook.
	/// </summary>
	[HttpGet("topup/success")]
	[ProducesResponseType(StatusCodes.Status302Found)]
	[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
	public async Task<IActionResult> TopUpSuccess(
		[FromQuery(Name = "session_id")] string? sessionId,
		[FromQuery] string? returnTarget)
	{
		try
		{
			var query = new Dictionary<string, string?>
			{
				["tab"] = "wallet",
				["topup"] = "success"
			};

			if (!string.IsNullOrWhiteSpace(sessionId))
			{
				query["session_id"] = sessionId.Trim();
			}

			var redirectUrl = QueryHelpers.AddQueryString(GetReturnUrl(returnTarget), query);
			await _accessLogService.LogAsync(User, "TopUpSuccess", true, $"sessionId={sessionId}");
			return Redirect(redirectUrl);
		}
		catch (InvalidOperationException ex)
		{
			_logger.LogError(ex, "Wallet top-up success return URL is not configured correctly");
			await _accessLogService.LogAsync(User, "TopUpSuccess", false, ex.Message);
			return StatusCode(
				StatusCodes.Status500InternalServerError,
				CreateError(StatusCodes.Status500InternalServerError, "The ParkJom return URL is not configured."));
		}
	}

	/// <summary>
	/// Stripe redirects the browser here if the user cancels checkout.
	/// Navigation only - no changes are made to the wallet.
	/// </summary>
	[HttpGet("topup/cancel")]
	[ProducesResponseType(StatusCodes.Status302Found)]
	[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
	public async Task<IActionResult> TopUpCancel([FromQuery] string? returnTarget)
	{
		try
		{
			var query = new Dictionary<string, string?>
			{
				["tab"] = "wallet",
				["topup"] = "cancelled"
			};

			var redirectUrl = QueryHelpers.AddQueryString(GetReturnUrl(returnTarget), query);
			await _accessLogService.LogAsync(User, "TopUpCancel", true);
			return Redirect(redirectUrl);
		}
		catch (InvalidOperationException ex)
		{
			_logger.LogError(ex, "Wallet top-up cancel return URL is not configured correctly");
			await _accessLogService.LogAsync(User, "TopUpCancel", false, ex.Message);
			return StatusCode(
				StatusCodes.Status500InternalServerError,
				CreateError(StatusCodes.Status500InternalServerError, "The ParkJom return URL is not configured."));
		}
	}

	private bool TryGetUserId(out int userId)
	{
		var id = _currentUser.UserId;
		userId = id ?? 0;
		return id.HasValue;
	}

	private static bool TryParsePaymentStatus(string? status, out PaymentStatus? paymentStatus)
	{
		paymentStatus = null;
		if (string.IsNullOrWhiteSpace(status))
		{
			return true;
		}

		var match = Enum.GetNames<PaymentStatus>()
			.FirstOrDefault(candidate => string.Equals(
				candidate,
				status.Trim(),
				StringComparison.OrdinalIgnoreCase));

		if (match == null)
		{
			return false;
		}

		paymentStatus = Enum.Parse<PaymentStatus>(match);
		return true;
	}

	private static WalletTopUpListItemResponse MapTopUpItem(Payment payment)
	{
		return new WalletTopUpListItemResponse
		{
			PaymentId = payment.PaymentId,
			Amount = payment.Amount,
			Currency = payment.Currency,
			Status = payment.Status.ToString(),
			SessionId = payment.StripeSessionId,
			CreatedAt = payment.CreatedAt,
			CompletedAt = payment.CompletedAt
		};
	}

	private string GetReturnUrl(string? returnTarget)
	{
		var isNative = string.Equals(returnTarget, "native", StringComparison.OrdinalIgnoreCase);
		var configurationKey = isNative ? "AppUrls:NativeReturnUrl" : "AppUrls:WebReturnUrl";
		var configuredUrl = _configuration[configurationKey]?.Trim();

		if (string.IsNullOrWhiteSpace(configuredUrl)
			|| !Uri.TryCreate(configuredUrl, UriKind.Absolute, out var uri)
			|| !IsAllowedReturnUri(uri, isNative))
		{
			throw new InvalidOperationException($"{configurationKey} is missing or invalid.");
		}

		return configuredUrl.TrimEnd('?','&');
	}

	private static bool IsAllowedReturnUri(Uri uri, bool isNative)
	{
		if (isNative)
		{
			return string.Equals(uri.Scheme, "parkjom", StringComparison.OrdinalIgnoreCase);
		}

		return string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
			|| (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && uri.IsLoopback);
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
