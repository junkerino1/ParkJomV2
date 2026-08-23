using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkJomV2.DTOs;
using ParkJomV2.Services;
using System.Text;
using Stripe;

namespace ParkJomV2.Controllers;

[ApiController]
[Route("api/stripe")]
public class StripeController : ControllerBase
{
    private readonly StripeService _stripeService;
    private readonly AccessLogService _accessLogService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<StripeController> _logger;

    public StripeController(StripeService stripeService, AccessLogService accessLogService, IConfiguration configuration, ILogger<StripeController> logger)
    {
        _stripeService = stripeService;
        _accessLogService = accessLogService;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> HandleWebhook()
    {
        var signature = Request.Headers["Stripe-Signature"].ToString();
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var payload = await reader.ReadToEndAsync();

        try
        {
            var webhookSecret = _configuration["Stripe:WebhookSecret"];
            if (string.IsNullOrWhiteSpace(webhookSecret))
            {
                await _accessLogService.LogAsync((int?)null, "StripeWebhook", false, "Webhook secret not configured");
                return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
                {
                    Code = StatusCodes.Status500InternalServerError,
                    Success = false,
                    Message = "Stripe webhook secret is not configured."
                });
            }

            var stripeEvent = EventUtility.ConstructEvent(payload, signature, webhookSecret);

            _logger.LogInformation("Stripe webhook received. EventType={EventType}, EventId={EventId}", stripeEvent.Type, stripeEvent.Id);

            await _stripeService.ProcessWebhookAsync(stripeEvent);

            await _accessLogService.LogAsync((int?)null, "StripeWebhook", true, $"EventType={stripeEvent.Type}");
            return Ok(new { received = true, processed = true });
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Invalid Stripe webhook signature");
            await _accessLogService.LogAsync((int?)null, "StripeWebhook", false, "Invalid signature");
            return BadRequest(new ErrorResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Success = false,
                Message = "Invalid Stripe webhook signature."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling Stripe webhook");
            await _accessLogService.LogAsync((int?)null, "StripeWebhook", false, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while handling the Stripe webhook."
            });
        }
    }
}
