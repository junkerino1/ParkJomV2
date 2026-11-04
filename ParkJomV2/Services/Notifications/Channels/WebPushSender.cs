using System.Net;
using WebPush;

namespace ParkJomV2.Services.Notifications.Channels;

public class WebPushSender : IWebPushSender
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<WebPushSender> _logger;

    public WebPushSender(IConfiguration configuration, ILogger<WebPushSender> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<WebPushSendResult> SendPushAsync(
        string endpoint,
        string p256dh,
        string auth,
        string payloadJson,
        CancellationToken ct = default)
    {
        var subject = _configuration["Notifications:WebPush:Subject"] ?? "mailto:support@parkjom.com";
        var publicKey = _configuration["Notifications:WebPush:PublicKey"];
        var privateKey = _configuration["Notifications:WebPush:PrivateKey"];

        if (string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(privateKey))
        {
            _logger.LogWarning("WebPush is enabled in channel delivery but VAPID keys are missing from configuration.");
            return WebPushSendResult.Failed("VAPID keys not configured");
        }

        try
        {
            var subscription = new PushSubscription(endpoint, p256dh, auth);
            var vapidDetails = new VapidDetails(subject, publicKey, privateKey);
            var client = new WebPushClient();

            await client.SendNotificationAsync(subscription, payloadJson, vapidDetails, ct);
            return WebPushSendResult.Succeeded();
        }
        catch (WebPushException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Gone || ex.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogInformation("WebPush subscription expired/unregistered for endpoint {Endpoint}", endpoint);
                return WebPushSendResult.Expired();
            }

            _logger.LogWarning(ex, "WebPush failed with HTTP {StatusCode}: {Message}", ex.StatusCode, ex.Message);
            return WebPushSendResult.Failed($"HTTP {ex.StatusCode}: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error dispatching WebPush to endpoint {Endpoint}", endpoint);
            return WebPushSendResult.Failed(ex.Message);
        }
    }
}
