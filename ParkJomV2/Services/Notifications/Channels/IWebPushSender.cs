namespace ParkJomV2.Services.Notifications.Channels;

public interface IWebPushSender
{
    Task<WebPushSendResult> SendPushAsync(
        string endpoint,
        string p256dh,
        string auth,
        string payloadJson,
        CancellationToken ct = default);
}

public class WebPushSendResult
{
    public bool Success { get; set; }
    public bool IsExpired { get; set; }
    public string? Error { get; set; }

    public static WebPushSendResult Succeeded() => new() { Success = true };
    public static WebPushSendResult Expired() => new() { Success = false, IsExpired = true, Error = "Subscription expired or unsubscribed (410/404)" };
    public static WebPushSendResult Failed(string error) => new() { Success = false, Error = error };
}
