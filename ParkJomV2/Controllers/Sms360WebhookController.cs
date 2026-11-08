using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;

namespace ParkJomV2.Controllers;

/// <summary>Receives delivery notifications from the SMS.360.my Bulk360 API.</summary>
[ApiController]
[AllowAnonymous]
[Route("api/webhooks/sms360")]
public sealed class Sms360WebhookController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<Sms360WebhookController> _logger;

    public Sms360WebhookController(
        ApplicationDbContext db,
        IConfiguration configuration,
        ILogger<Sms360WebhookController> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// SMS.360.my calls this endpoint after the carrier reports delivery status.
    /// Configure the provider hook as /api/webhooks/sms360/delivery/{Sms360:WebhookSecret}.
    /// </summary>
    [HttpGet("delivery/{secret}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ReceiveDeliveryNotification(
        [FromRoute] string secret,
        [FromQuery] string? status,
        [FromQuery(Name = "error-code")] string? errorCode,
        [FromQuery] string? msisdn,
        [FromQuery] string? msgid,
        [FromQuery(Name = "operator")] string? mobileOperator,
        CancellationToken cancellationToken)
    {
        var expectedSecret = _configuration["Sms360:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(expectedSecret))
        {
            _logger.LogError("SMS360 webhook rejected because Sms360:WebhookSecret is not configured.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        if (!FixedTimeEquals(secret, expectedSecret))
            return Unauthorized();
        if (string.IsNullOrWhiteSpace(status) || string.IsNullOrWhiteSpace(msgid))
            return BadRequest(new { success = false, message = "status and msgid are required." });

        var otp = await _db.PhoneOtpVerifications
            .FirstOrDefaultAsync(item => item.ProviderMessageId == msgid, cancellationToken);
        if (otp is null)
        {
            // The provider callback is acknowledged to prevent an endless retry loop.
            // This can occur if the provider calls back before the send response is stored.
            _logger.LogWarning("SMS360 delivery notification has no matching ParkJom OTP record. Status={Status}", status);
            return Ok(new { success = true, matched = false });
        }

        var normalizedStatus = status.Trim().ToUpperInvariant();
        if (!(normalizedStatus == "SENT" && otp.SmsDeliveryStatus == "Delivered"))
        {
            otp.SmsDeliveryStatus = normalizedStatus switch
            {
                "DELIVERED" => "Delivered",
                "UNDELIVERED" => "Undelivered",
                "SENT" => "SentToCarrier",
                _ => "Unknown"
            };
        }

        if (!string.IsNullOrWhiteSpace(errorCode))
            otp.SmsErrorCode = errorCode.Length <= 10 ? errorCode : errorCode[..10];
        if (otp.SmsDeliveryStatus == "Undelivered")
            otp.SmsErrorDescription = DescribeDeliveryError(errorCode);
        else
            otp.SmsErrorDescription = null;
        if (!string.IsNullOrWhiteSpace(mobileOperator))
            otp.MobileOperator = mobileOperator.Length <= 50 ? mobileOperator : mobileOperator[..50];
        if (otp.SmsDeliveryStatus == "Delivered")
            otp.SmsDeliveredAtUtc = DateTime.UtcNow;
        otp.UpdatedAtUtc = DateTime.UtcNow;

        // Guard against a callback payload associating a reference with a different number.
        if (!string.IsNullOrWhiteSpace(msisdn))
        {
            var callbackPhone = NormalizePhone(msisdn);
            if (callbackPhone is null || !string.Equals(callbackPhone, otp.PhoneNumber, StringComparison.Ordinal))
            {
                _logger.LogWarning("SMS360 delivery notification phone did not match its message reference.");
                return BadRequest(new { success = false, message = "Phone number does not match message reference." });
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new { success = true, matched = true });
    }

    private static bool FixedTimeEquals(string supplied, string expected)
    {
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return suppliedBytes.Length == expectedBytes.Length && CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
    }

    private static string? NormalizePhone(string value)
    {
        var digits = new string(value.Where(character => character is >= '0' and <= '9').ToArray());
        if (digits.StartsWith("0", StringComparison.Ordinal)) digits = "60" + digits[1..];
        return digits.StartsWith("60", StringComparison.Ordinal) ? digits : null;
    }

    private static string DescribeDeliveryError(string? errorCode) => errorCode switch
    {
        "501" or "516" => "The phone appears unreachable or switched off.",
        "502" => "The recipient's phone cannot store more messages.",
        "505" or "515" => "The mobile network could not identify this subscriber.",
        "553" => "SMS delivery failed because of a credit or destination restriction.",
        "554" => "The mobile number is invalid.",
        "558" => "The SMS provider throttled the message.",
        "571" => "The recipient has opted out of SMS messages.",
        "575" => "The mobile network rejected the SMS content.",
        _ => "The mobile network reported that the SMS was not delivered."
    };
}
