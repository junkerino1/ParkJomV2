using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Hubs;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services.Notifications.Channels;
using ParkJomV2.Services.Notifications.NotificationTemplates;

namespace ParkJomV2.Services.Notifications.Workers;

public class NotificationDispatchWorker : BackgroundService
{
    private readonly INotificationQueue _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<NotificationDispatchWorker> _logger;

    public NotificationDispatchWorker(
        INotificationQueue queue,
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<NotificationDispatchWorker> logger)
    {
        _queue = queue;
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("NotificationDispatchWorker started.");

        // Process real-time queue
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var notificationId = await _queue.DequeueAsync(stoppingToken);
                await ProcessNotificationAsync(notificationId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing queued notification in dispatch worker.");
                await Task.Delay(1000, stoppingToken);
            }
        }

        _logger.LogInformation("NotificationDispatchWorker stopped.");
    }

    private async Task ProcessNotificationAsync(long notificationId, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<NotificationHub>>();
        var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        var notification = await context.Notifications
            .Include(n => n.Deliveries)
            .Include(n => n.User)
            .FirstOrDefaultAsync(n => n.NotificationId == notificationId, ct);

        if (notification == null) return;

        var appBaseUrl = _configuration["Notifications:AppBaseUrl"] ?? "https://united-perigee-400000.web.app";

        // 1. In-App SignalR Delivery
        var inAppDelivery = notification.Deliveries
            .FirstOrDefault(d => d.Channel == NotificationChannel.InApp && d.Status == DeliveryStatus.Pending);

        if (inAppDelivery != null)
        {
            try
            {
                var dto = MapToDto(notification);

                await hubContext.Clients.Group($"User-{notification.UserId}")
                    .SendAsync("notification.created", dto, ct);

                var unreadItems = await context.Notifications
                    .AsNoTracking()
                    .Where(n => n.UserId == notification.UserId && n.ReadAt == null && n.ArchivedAt == null)
                    .Select(n => n.Category)
                    .ToListAsync(ct);

                var unreadCountDto = new UnreadCountDto
                {
                    Total = unreadItems.Count,
                    ByCategory = unreadItems.GroupBy(c => c.ToString()).ToDictionary(g => g.Key, g => g.Count())
                };

                await hubContext.Clients.Group($"User-{notification.UserId}")
                    .SendAsync("notification.unreadCount", unreadCountDto, ct);

                inAppDelivery.Status = DeliveryStatus.Sent;
                inAppDelivery.SentAt = DateTime.UtcNow;
                inAppDelivery.AttemptCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deliver InApp SignalR notification {NotificationId} to User {UserId}",
                    notification.NotificationId, notification.UserId);
                inAppDelivery.LastError = ex.Message;
                inAppDelivery.AttemptCount++;
            }
        }

        // 2. Email Delivery
        var emailDelivery = notification.Deliveries
            .FirstOrDefault(d => d.Channel == NotificationChannel.Email && d.Status == DeliveryStatus.Pending);

        if (emailDelivery != null && notification.User != null && !string.IsNullOrWhiteSpace(notification.User.Email))
        {
            try
            {
                var (subject, htmlBody, textBody) = NotificationEmailTemplates.RenderEmail(notification, notification.User, appBaseUrl);

                var sendResult = await emailSender.SendEmailAsync(
                    notification.User.Email,
                    $"{notification.User.FirstName} {notification.User.LastName}".Trim(),
                    subject,
                    htmlBody,
                    textBody,
                    ct);

                emailDelivery.AttemptCount++;

                if (sendResult.Success)
                {
                    emailDelivery.Status = DeliveryStatus.Sent;
                    emailDelivery.SentAt = DateTime.UtcNow;
                    emailDelivery.ProviderMessageId = sendResult.MessageId;
                }
                else
                {
                    emailDelivery.LastError = sendResult.Error;
                    if (emailDelivery.AttemptCount >= 5)
                    {
                        emailDelivery.Status = DeliveryStatus.Failed;
                    }
                    else
                    {
                        // Exponential backoff: 30s, 2m, 8m, 32m
                        var backoffSecs = (int)Math.Pow(4, emailDelivery.AttemptCount) * 15;
                        emailDelivery.NextAttemptAt = DateTime.UtcNow.AddSeconds(backoffSecs);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception delivering email for notification {NotificationId}", notification.NotificationId);
                emailDelivery.AttemptCount++;
                emailDelivery.LastError = ex.Message;
                if (emailDelivery.AttemptCount >= 5)
                {
                    emailDelivery.Status = DeliveryStatus.Failed;
                }
                else
                {
                    emailDelivery.NextAttemptAt = DateTime.UtcNow.AddMinutes(5);
                }
            }
        }

        // 3. Web Push Delivery
        var webPushDelivery = notification.Deliveries
            .FirstOrDefault(d => d.Channel == NotificationChannel.WebPush && d.Status == DeliveryStatus.Pending);

        if (webPushDelivery != null)
        {
            try
            {
                var webPushSender = scope.ServiceProvider.GetRequiredService<IWebPushSender>();
                var subscriptions = await context.PushSubscriptions
                    .Where(s => s.UserId == notification.UserId)
                    .ToListAsync(ct);

                if (subscriptions.Count == 0)
                {
                    webPushDelivery.Status = DeliveryStatus.Sent;
                    webPushDelivery.SentAt = DateTime.UtcNow;
                    webPushDelivery.LastError = "No active push subscriptions registered";
                }
                else
                {
                    var payload = JsonSerializer.Serialize(new
                    {
                        title = notification.Title,
                        body = notification.Body,
                        url = notification.ActionUrl ?? "/",
                        tag = $"notification-{notification.NotificationId}",
                        icon = "/icon-192.png",
                        badge = "/badge-72.png",
                        data = new
                        {
                            notificationId = notification.NotificationId,
                            category = notification.Category.ToString(),
                            actionUrl = notification.ActionUrl ?? "/"
                        }
                    });

                    var expiredSubscriptions = new List<Models.PushSubscription>();
                    var sentCount = 0;
                    string? lastErr = null;

                    foreach (var sub in subscriptions)
                    {
                        var res = await webPushSender.SendPushAsync(sub.Endpoint, sub.P256dh, sub.Auth, payload, ct);
                        if (res.Success)
                        {
                            sentCount++;
                            sub.LastUsedAt = DateTime.UtcNow;
                        }
                        else if (res.IsExpired)
                        {
                            expiredSubscriptions.Add(sub);
                        }
                        else
                        {
                            lastErr = res.Error;
                        }
                    }

                    if (expiredSubscriptions.Count > 0)
                    {
                        context.PushSubscriptions.RemoveRange(expiredSubscriptions);
                    }

                    webPushDelivery.AttemptCount++;
                    if (sentCount > 0 || subscriptions.Count == expiredSubscriptions.Count)
                    {
                        webPushDelivery.Status = DeliveryStatus.Sent;
                        webPushDelivery.SentAt = DateTime.UtcNow;
                    }
                    else
                    {
                        webPushDelivery.LastError = lastErr ?? "Failed to send to push endpoints";
                        if (webPushDelivery.AttemptCount >= 3)
                        {
                            webPushDelivery.Status = DeliveryStatus.Failed;
                        }
                        else
                        {
                            webPushDelivery.NextAttemptAt = DateTime.UtcNow.AddMinutes(2);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in web push delivery for notification {NotificationId}", notification.NotificationId);
                webPushDelivery.AttemptCount++;
                webPushDelivery.LastError = ex.Message;
                if (webPushDelivery.AttemptCount >= 3)
                {
                    webPushDelivery.Status = DeliveryStatus.Failed;
                }
                else
                {
                    webPushDelivery.NextAttemptAt = DateTime.UtcNow.AddMinutes(5);
                }
            }
        }

        await context.SaveChangesAsync(ct);
    }

    public async Task<int> RunRetrySweepOnceAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTime.UtcNow;
        var pendingDeliveries = await context.NotificationDeliveries
            .Where(d => d.Status == DeliveryStatus.Pending &&
                        (d.NextAttemptAt == null || d.NextAttemptAt <= now))
            .Select(d => d.NotificationId)
            .Distinct()
            .Take(50)
            .ToListAsync(ct);

        foreach (var id in pendingDeliveries)
        {
            await ProcessNotificationAsync(id, ct);
        }
        return pendingDeliveries.Count;
    }

    private static NotificationDto MapToDto(Notification n)
    {
        Dictionary<string, object?>? data = null;
        if (!string.IsNullOrEmpty(n.DataJson))
        {
            try
            {
                data = JsonSerializer.Deserialize<Dictionary<string, object?>>(n.DataJson);
            }
            catch
            {
                // ignore parsing failure
            }
        }

        return new NotificationDto
        {
            Id = n.NotificationId,
            Type = n.Type,
            Category = n.Category.ToString(),
            Priority = n.Priority.ToString(),
            Title = n.Title,
            Body = n.Body,
            ActionUrl = n.ActionUrl,
            Entity = !string.IsNullOrEmpty(n.EntityType) || !string.IsNullOrEmpty(n.EntityId)
                ? new NotificationEntityDto { Type = n.EntityType, Id = n.EntityId }
                : null,
            Data = data,
            CreatedAt = n.CreatedAt,
            ReadAt = n.ReadAt
        };
    }
}
