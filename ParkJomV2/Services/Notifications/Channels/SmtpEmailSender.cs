using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using System.Net.Sockets;

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

            // Never report a successful send when SMTP is not configured. Invitation
            // creation depends on the email being deliverable.
            var missingSettings = new List<string>();
            if (string.IsNullOrWhiteSpace(host)) missingSettings.Add("Notifications:Email:Smtp:Host");
            if (string.IsNullOrWhiteSpace(user)) missingSettings.Add("Notifications:Email:Smtp:User");
            if (string.IsNullOrWhiteSpace(pass)) missingSettings.Add("Notifications:Email:Smtp:Password");
            if (missingSettings.Count > 0)
            {
                var error = $"SMTP is not configured. Missing required setting(s): {string.Join(", ", missingSettings)}.";
                _logger.LogError("{Error}", error);
                return EmailSendResult.Failed(error, "SMTP_CONFIGURATION_MISSING");
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
            await client.ConnectAsync(host!, port, socketOptions, ct);

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
        catch (MailKit.Security.AuthenticationException ex)
        {
            _logger.LogError(ex, "Failed to send email to {RecipientEmail} | Subject: {Subject}", recipientEmail, subject);
            return EmailSendResult.Failed("The SMTP server rejected authentication. Verify the Brevo SMTP login and SMTP key (not the API key).", "SMTP_AUTHENTICATION_FAILED");
        }
        catch (SmtpCommandException ex)
        {
            _logger.LogError(ex, "SMTP rejected email delivery to {RecipientEmail} | Subject: {Subject} | Status: {StatusCode}", recipientEmail, subject, ex.StatusCode);
            return EmailSendResult.Failed($"The SMTP server rejected the message (status {(int)ex.StatusCode} {ex.StatusCode}). Check the verified sender address and Brevo SMTP permissions.", "SMTP_MESSAGE_REJECTED");
        }
        catch (SmtpProtocolException ex)
        {
            _logger.LogError(ex, "SMTP protocol error sending email to {RecipientEmail} | Subject: {Subject}", recipientEmail, subject);
            return EmailSendResult.Failed("The SMTP connection was established, but the server returned an unexpected SMTP response. Check the host, port, and TLS mode.", "SMTP_PROTOCOL_ERROR");
        }
        catch (SocketException ex)
        {
            _logger.LogError(ex, "Could not connect to SMTP server for {RecipientEmail} | Subject: {Subject}", recipientEmail, subject);
            return EmailSendResult.Failed("Could not connect to the SMTP server. Check the host, port, TLS mode, and outbound network access.", "SMTP_CONNECTION_FAILED");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {RecipientEmail} | Subject: {Subject}", recipientEmail, subject);
            return EmailSendResult.Failed($"SMTP delivery failed ({ex.GetType().Name}). Check the application logs for the detailed exception.", "SMTP_SEND_FAILED");
        }
    }
}
