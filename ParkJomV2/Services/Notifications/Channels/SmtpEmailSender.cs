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
            var host = emailConfig["Smtp:Host"]?.Trim();
            var portText = emailConfig["Smtp:Port"]?.Trim();
            var user = emailConfig["Smtp:User"]?.Trim();
            var pass = emailConfig["Smtp:Password"]?.Replace(" ", "").Trim();
            var from = emailConfig["From"]?.Trim() ?? "ParkJom <no-reply@parkjom.com>";

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
            var socketOptions = port switch
            {
                465 => SecureSocketOptions.SslOnConnect,
                587 => SecureSocketOptions.StartTls,
                25 => SecureSocketOptions.StartTlsWhenAvailable,
                _ => enableSsl ? SecureSocketOptions.Auto : SecureSocketOptions.None
            };

            // If using Gmail or from is default @parkjom.com, align From with authenticated account
            var effectiveFrom = from;
            if (!string.IsNullOrWhiteSpace(user) && user.Contains('@'))
            {
                var isDefaultDomain = from.Contains("@parkjom.com", StringComparison.OrdinalIgnoreCase);
                var isGmail = (host?.Contains("gmail.com", StringComparison.OrdinalIgnoreCase) ?? false) ||
                              user.EndsWith("@gmail.com", StringComparison.OrdinalIgnoreCase);
                if (isDefaultDomain || isGmail)
                {
                    effectiveFrom = $"ParkJom <{user}>";
                }
            }

            var message = new MimeMessage();
            if (MailboxAddress.TryParse(effectiveFrom, out var fromAddress))
            {
                message.From.Add(fromAddress);
            }
            else
            {
                message.From.Add(new MailboxAddress("ParkJom", effectiveFrom));
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
            client.Timeout = 15000;
            client.ServerCertificateValidationCallback = (s, c, h, e) => true;

            await client.ConnectAsync(host!, port, socketOptions, ct);

            if (!string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(pass))
            {
                await client.AuthenticateAsync(user, pass, ct);
            }

            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);

            _logger.LogInformation("Email successfully dispatched to {RecipientEmail} | Subject: {Subject}",
                recipientEmail, subject);

            return EmailSendResult.Succeeded($"smtp-{DateTime.UtcNow.Ticks}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {RecipientEmail} | Subject: {Subject} | Error: {Message}",
                recipientEmail, subject, ex.Message);
            return EmailSendResult.Failed(ex.Message);
        }
    }
}
