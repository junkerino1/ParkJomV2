using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using ParkJomV2.DTOs;
using ParkJomV2.Services;
using System.Security.Claims;

namespace ParkJomV2.Controllers;

[ApiController]
[Route("api/wallet")]
public class WalletTopUpController : ControllerBase
{
	private readonly StripeService _stripeService;
	private readonly IConfiguration _configuration;
	private readonly ILogger<WalletTopUpController> _logger;

	public WalletTopUpController(
		StripeService stripeService,
		IConfiguration configuration,
		ILogger<WalletTopUpController> logger)
	{
		_stripeService = stripeService;
		_configuration = configuration;
		_logger = logger;
	}

	[Authorize]
	[HttpGet]
	[ProducesResponseType(typeof(WalletSummaryResponse), StatusCodes.Status200OK)]
	[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
	[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
	[ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
	public async Task<ActionResult<WalletSummaryResponse>> GetWallet(CancellationToken cancellationToken)
	{
		if (!TryGetUserId(out var userId))
		{
			return Unauthorized(CreateError(StatusCodes.Status401Unauthorized, "Authentication required."));
		}

		try
		{
			return Ok(await _stripeService.GetWalletAsync(userId, cancellationToken));
		}
		catch (KeyNotFoundException ex)
		{
			return NotFound(CreateError(StatusCodes.Status404NotFound, ex.Message));
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error retrieving wallet for user {UserId}", userId);
			return StatusCode(
				StatusCodes.Status500InternalServerError,
				CreateError(StatusCodes.Status500InternalServerError, "An error occurred while retrieving the wallet."));
		}
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
				return Unauthorized(CreateError(StatusCodes.Status401Unauthorized, "Authentication required."));
			}

			var result = await _stripeService.CreateTopUpSessionAsync(userId, request);

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
			return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
			{
				Code = StatusCodes.Status500InternalServerError,
				Success = false,
				Message = "An error occurred while creating the wallet top-up session."
			});
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
			return Unauthorized(CreateError(StatusCodes.Status401Unauthorized, "Authentication required."));
		}

		if (string.IsNullOrWhiteSpace(sessionId))
		{
			return BadRequest(CreateError(
				StatusCodes.Status400BadRequest,
				"A Stripe checkout session ID is required."));
		}

		try
		{
			return Ok(await _stripeService.GetTopUpStatusAsync(
				userId,
				sessionId,
				cancellationToken));
		}
		catch (KeyNotFoundException ex)
		{
			return NotFound(CreateError(StatusCodes.Status404NotFound, ex.Message));
		}
		catch (InvalidOperationException ex)
		{
			return BadRequest(CreateError(StatusCodes.Status400BadRequest, ex.Message));
		}
		catch (Exception ex)
		{
			_logger.LogError(
				ex,
				"Error retrieving wallet top-up status for user {UserId}, session {SessionId}",
				userId,
				sessionId);
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
	public IActionResult TopUpSuccess(
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

			return Redirect(QueryHelpers.AddQueryString(GetReturnUrl(returnTarget), query));
		}
		catch (InvalidOperationException ex)
		{
			_logger.LogError(ex, "Wallet top-up success return URL is not configured correctly");
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
	public IActionResult TopUpCancel([FromQuery] string? returnTarget)
	{
		try
		{
			var query = new Dictionary<string, string?>
			{
				["tab"] = "wallet",
				["topup"] = "cancelled"
			};

			return Redirect(QueryHelpers.AddQueryString(GetReturnUrl(returnTarget), query));
		}
		catch (InvalidOperationException ex)
		{
			_logger.LogError(ex, "Wallet top-up cancel return URL is not configured correctly");
			return StatusCode(
				StatusCodes.Status500InternalServerError,
				CreateError(StatusCodes.Status500InternalServerError, "The ParkJom return URL is not configured."));
		}
	}

	private bool TryGetUserId(out int userId)
	{
		return int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
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
