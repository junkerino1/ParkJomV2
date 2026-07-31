using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkJomV2.DTOs;
using ParkJomV2.Services;
using System.Security.Claims;

namespace ParkJomV2.Controllers;

[ApiController]
[Route("api/wallet")]
public class WalletTopUpController : ControllerBase
{
	private readonly StripeService _stripeService;
	private readonly ILogger<WalletTopUpController> _logger;

	public WalletTopUpController(StripeService stripeService, ILogger<WalletTopUpController> logger)
	{
		_stripeService = stripeService;
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
			return BadRequest(new ErrorResponse
			{
				Code = StatusCodes.Status400BadRequest,
				Success = false,
				Message = "Invalid request."
			});
		}

		try
		{
			var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
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

	/// <summary>
	/// Stripe redirects the browser here after a successful payment.
	/// Navigation only - the wallet is credited by the webhook.
	/// </summary>
	[HttpGet("topup/success")]
	[ProducesResponseType(StatusCodes.Status200OK)]
	public IActionResult TopUpSuccess([FromQuery] string? session_id)
	{
		return Ok(new
		{
			Code = StatusCodes.Status200OK,
			Success = true,
			Message = "Top-up successful. Your wallet has been credited.",
			SessionId = session_id
		});
	}

	/// <summary>
	/// Stripe redirects the browser here if the user cancels checkout.
	/// Navigation only - no changes are made to the wallet.
	/// </summary>
	[HttpGet("topup/cancel")]
	[ProducesResponseType(StatusCodes.Status200OK)]
	public IActionResult TopUpCancel()
	{
		return Ok(new
		{
			Code = StatusCodes.Status200OK,
			Success = false,
			Message = "Top-up cancelled. No changes were made to your wallet."
		});
	}
}
