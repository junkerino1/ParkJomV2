namespace ParkJomV2.Services.Notifications.Channels;

public interface IEmailSender
{
    Task<EmailSendResult> SendEmailAsync(
        string recipientEmail,
        string recipientName,
        string subject,
        string htmlBody,
        string textBody,
        CancellationToken ct = default);
}

public class EmailSendResult
{
    public bool Success { get; set; }
    public string? MessageId { get; set; }
    public string? Error { get; set; }

    public static EmailSendResult Succeeded(string? messageId = null) => new() { Success = true, MessageId = messageId };
    public static EmailSendResult Failed(string error) => new() { Success = false, Error = error };
}
