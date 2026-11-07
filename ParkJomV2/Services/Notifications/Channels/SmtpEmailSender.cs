using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace ParkJomV2.Services.Notifications.Channels;

public class SmtpEmailSender : IEmailSender
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IConfiguration configuration, ILogger<SmtpEmailSender> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<EmailSendResult> SendEmailAsync(
        string recipientEmail,
        string recipientName,
        string subject,
        string htmlBody,
        string textBody,
        CancellationToken ct = default)
    {
        try
        {
            var emailConfig = _configuration.GetSection("Notifications:Email");
            var host = emailConfig["Smtp:Host"];
            var portText = emailConfig["Smtp:Port"];
            var user = emailConfig["Smtp:User"];
            var pass = emailConfig["Smtp:Password"];
            var from = emailConfig["From"] ?? "ParkJom <no-reply@parkjom.com>";

            // If SMTP host or password is not configured, run in simulation mode
            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(pass))
            {
                // The body can contain security-sensitive links, so never include it in logs.
                _logger.LogInformation(
                    "[EMAIL SIMULATION] To: {RecipientEmail} ({RecipientName}) | Subject: {Subject}",
                    recipientEmail, recipientName, subject);

                return EmailSendResult.Succeeded($"sim-{Guid.NewGuid():N}", simulated: true);
            }

            var port = int.TryParse(portText, out var p) ? p : 587;
            var enableSsl = bool.TryParse(emailConfig["Smtp:EnableSsl"], out var ssl) ? ssl : true;
            var socketOptions = port == 465
                ? SecureSocketOptions.SslOnConnect
                : enableSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.None;

            var message = new MimeMessage();
            if (MailboxAddress.TryParse(from, out var fromAddress))
            {
                message.From.Add(fromAddress);
            }
            else
            {
                message.From.Add(new MailboxAddress("ParkJom", from));
            }

            message.To.Add(new MailboxAddress(recipientName, recipientEmail));
            message.Subject = subject;

            var builder = new BodyBuilder
            {
                HtmlBody = htmlBody,
                TextBody = textBody
            };
            message.Body = builder.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(host, port, socketOptions, ct);

            if (!string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(pass))
            {
                await client.AuthenticateAsync(user.Trim(), pass.Trim(), ct);
            }

            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);

            _logger.LogInformation("Email successfully dispatched to {RecipientEmail} | Subject: {Subject}",
                recipientEmail, subject);

            return EmailSendResult.Succeeded($"smtp-{DateTime.UtcNow.Ticks}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {RecipientEmail} | Subject: {Subject}", recipientEmail, subject);
            return EmailSendResult.Failed(ex.Message);
        }
    }
}
