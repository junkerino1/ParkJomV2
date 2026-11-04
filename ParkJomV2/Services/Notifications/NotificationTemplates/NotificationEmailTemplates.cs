using System.Net;
using System.Text;
using ParkJomV2.Models;

namespace ParkJomV2.Services.Notifications.NotificationTemplates;

public static class NotificationEmailTemplates
{
    public static (string Subject, string HtmlBody, string TextBody) RenderEmail(
        Notification notification,
        User recipient,
        string appBaseUrl)
    {
        var subject = $"[ParkJom] {notification.Title}";
        var recipientName = !string.IsNullOrWhiteSpace(recipient.FirstName)
            ? $"{recipient.FirstName} {recipient.LastName}".Trim()
            : "ParkJom Member";

        var actionUrl = !string.IsNullOrWhiteSpace(notification.ActionUrl)
            ? (notification.ActionUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? notification.ActionUrl
                : $"{appBaseUrl.TrimEnd('/')}/{notification.ActionUrl.TrimStart('/')}")
            : appBaseUrl;

        var htmlBuilder = new StringBuilder();
        htmlBuilder.Append($@"<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta charset=""UTF-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
  <title>{WebUtility.HtmlEncode(notification.Title)}</title>
</head>
<body style=""margin: 0; padding: 0; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; background-color: #f8fafc; color: #1e293b;"">
  <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" style=""background-color: #f8fafc; padding: 24px 12px;"">
    <tr>
      <td align=""center"">
        <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" width=""100%"" style=""max-width: 580px; background-color: #ffffff; border-radius: 12px; box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.05); overflow: hidden; border: 1px solid #e2e8f0;"">
          <!-- Header -->
          <tr>
            <td style=""background: linear-gradient(135deg, #0f172a 0%, #1e293b 100%); padding: 24px 32px; text-align: left;"">
              <span style=""font-size: 22px; font-weight: 800; color: #10b981; letter-spacing: -0.5px;"">ParkJom</span>
              <span style=""font-size: 13px; color: #94a3b8; margin-left: 8px;"">Smart Transit Parking</span>
            </td>
          </tr>
          <!-- Body Content -->
          <tr>
            <td style=""padding: 32px;"">
              <h2 style=""margin: 0 0 16px 0; font-size: 20px; font-weight: 700; color: #0f172a;"">{WebUtility.HtmlEncode(notification.Title)}</h2>
              <p style=""margin: 0 0 16px 0; font-size: 15px; line-height: 1.6; color: #334155;"">Hi {WebUtility.HtmlEncode(recipientName)},</p>
              <div style=""background-color: #f1f5f9; border-left: 4px solid #10b981; padding: 16px 20px; border-radius: 4px; margin-bottom: 24px;"">
                <p style=""margin: 0; font-size: 15px; line-height: 1.6; color: #1e293b;"">{WebUtility.HtmlEncode(notification.Body)}</p>
              </div>
              <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" style=""margin-top: 24px;"">
                <tr>
                  <td align=""center"" style=""border-radius: 8px; background: #10b981;"">
                    <a href=""{actionUrl}"" target=""_blank"" style=""font-size: 15px; font-weight: 600; color: #ffffff; text-decoration: none; padding: 12px 28px; display: inline-block; border-radius: 8px;"">View Details on ParkJom &rarr;</a>
                  </td>
                </tr>
              </table>
            </td>
          </tr>
          <!-- Footer -->
          <tr>
            <td style=""background-color: #f8fafc; padding: 20px 32px; text-align: center; border-top: 1px solid #e2e8f0; font-size: 12px; color: #64748b;"">
              <p style=""margin: 0 0 8px 0;"">You received this because of your account activity on ParkJom.</p>
              <p style=""margin: 0;""><a href=""{appBaseUrl}/profile"" style=""color: #10b981; text-decoration: underline;"">Manage notification preferences</a> · <a href=""{appBaseUrl}/support"" style=""color: #64748b; text-decoration: underline;"">Support</a></p>
            </td>
          </tr>
        </table>
      </td>
    </tr>
  </table>
</body>
</html>");

        var textBuilder = new StringBuilder();
        textBuilder.AppendLine($"[ParkJom] {notification.Title}");
        textBuilder.AppendLine();
        textBuilder.AppendLine($"Hi {recipientName},");
        textBuilder.AppendLine();
        textBuilder.AppendLine(notification.Body);
        textBuilder.AppendLine();
        textBuilder.AppendLine($"View details: {actionUrl}");
        textBuilder.AppendLine();
        textBuilder.AppendLine("Manage your preferences at: " + $"{appBaseUrl}/profile");

        return (Subject: subject, HtmlBody: htmlBuilder.ToString(), TextBody: textBuilder.ToString());
    }
}
