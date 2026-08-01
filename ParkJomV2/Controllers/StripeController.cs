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
    private readonly IConfiguration _configuration;
    private readonly ILogger<StripeController> _logger;

    public StripeController(StripeService stripeService, IConfiguration configuration, ILogger<StripeController> logger)
    {
        _stripeService = stripeService;
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

        _logger.LogInformation("Received Stripe webhook. Payload: {Payload}, Signature: {Signature}", payload, signature);

        try
        {
            var webhookSecret = _configuration["Stripe:WebhookSecret"];
            if (string.IsNullOrWhiteSpace(webhookSecret))
            {
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

            return Ok(new { received = true, processed = true });
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Invalid Stripe webhook signature");
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
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "An error occurred while handling the Stripe webhook."
            });
        }
    }
}
