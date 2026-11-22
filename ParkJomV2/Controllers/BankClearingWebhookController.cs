using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ParkJomV2.DTOs;
using ParkJomV2.Services;
using ParkJomV2.Services.Settlement;

namespace ParkJomV2.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/webhooks/bank-clearing")]
public sealed class BankClearingWebhookController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly SettlementService _settlementService;
    private readonly AccessLogService _accessLogService;
    private readonly ILogger<BankClearingWebhookController> _logger;

    public BankClearingWebhookController(
        IConfiguration configuration,
        SettlementService settlementService,
        AccessLogService accessLogService,
        ILogger<BankClearingWebhookController> logger)
    {
        _configuration = configuration;
        _settlementService = settlementService;
        _accessLogService = accessLogService;
        _logger = logger;
    }

    [HttpPost]
    [ProducesResponseType(typeof(SettlementBatchDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> HandleBankClearingWebhook(CancellationToken ct)
    {
        var configuredSecret = _configuration["BankClearing:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(configuredSecret))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ErrorResponse
            {
                Code = StatusCodes.Status503ServiceUnavailable,
                Success = false,
                Message = "Bank clearing webhook secret is not configured (BankClearing:WebhookSecret)."
            });
        }

        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var rawPayload = await reader.ReadToEndAsync(ct);
        var suppliedSignature = Request.Headers["X-Bank-Webhook-Signature"].FirstOrDefault()?.Trim() ?? string.Empty;

        var expectedHash = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(configuredSecret),
            Encoding.UTF8.GetBytes(rawPayload));
        var expectedHex = Convert.ToHexString(expectedHash).ToLowerInvariant();
        var normalizedSupplied = suppliedSignature.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)
            ? suppliedSignature[7..].ToLowerInvariant()
            : suppliedSignature.ToLowerInvariant();

        var expectedBytes = Encoding.UTF8.GetBytes(expectedHex);
        var suppliedBytes = Encoding.UTF8.GetBytes(normalizedSupplied);

        if (suppliedBytes.Length != expectedBytes.Length ||
            !CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes))
        {
            await _accessLogService.LogAsync((int?)null, "BankClearingWebhook", false, "Invalid X-Bank-Webhook-Signature.");
            return Unauthorized(new ErrorResponse
            {
                Code = StatusCodes.Status401Unauthorized,
                Success = false,
                Message = "Invalid bank webhook signature."
            });
        }

        BankClearingWebhookRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<BankClearingWebhookRequest>(
                rawPayload,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return BadRequest(new ErrorResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Success = false,
                Message = "Malformed JSON payload."
            });
        }

        if (request is null || string.IsNullOrWhiteSpace(request.BatchReference))
        {
            return BadRequest(new ErrorResponse
            {
                Code = StatusCodes.Status400BadRequest,
                Success = false,
                Message = "batchReference is required."
            });
        }

        try
        {
            var detail = await _settlementService.ApplyBankClearingUpdateAsync(
                null,
                request.BatchReference,
                request.BankReference,
                request.Items,
                actorUserId: null,
                source: "BankClearingWebhook",
                ct);

            return Ok(new SettlementBatchDetailResponse
            {
                Code = StatusCodes.Status200OK,
                Success = true,
                Message = $"Webhook processed clearing update for {detail.Batch.BatchReference}.",
                Data = detail
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ErrorResponse { Code = StatusCodes.Status404NotFound, Success = false, Message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ErrorResponse { Code = StatusCodes.Status400BadRequest, Success = false, Message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing bank clearing webhook for batch {BatchReference}", request.BatchReference);
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                Code = StatusCodes.Status500InternalServerError,
                Success = false,
                Message = "Failed to process bank clearing webhook."
            });
        }
    }
}
