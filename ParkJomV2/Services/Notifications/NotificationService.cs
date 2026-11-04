using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Hubs;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.Services.Notifications;

public class NotificationService : INotificationService
{
    private readonly ApplicationDbContext _context;
    private readonly INotificationQueue _queue;
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        ApplicationDbContext context,
        INotificationQueue queue,
        IHubContext<NotificationHub> hubContext,
        ILogger<NotificationService> logger)
    {
        _context = context;
        _queue = queue;
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task<Notification?> PublishAsync(NotificationPublishRequest request, CancellationToken ct = default)
    {
        try
        {
            // 1. Deduplication check
            if (!string.IsNullOrWhiteSpace(request.DedupeKey))
            {
                var exists = await _context.Notifications
                    .AnyAsync(n => n.UserId == request.UserId && n.DedupeKey == request.DedupeKey, ct);
                if (exists)
                {
                    _logger.LogInformation("Skipping duplicate notification for User {UserId} with DedupeKey {DedupeKey}",
                        request.UserId, request.DedupeKey);
                    return null;
                }
            }

            // 2. Resolve definition from catalog
            var definition = NotificationCatalog.Get(request.Type);
            var category = definition?.Category ?? NotificationCategory.System;
            var priority = request.PriorityOverride ?? definition?.DefaultPriority ?? NotificationPriority.Normal;

            string title;
            string body;
            string? defaultActionUrl = null;

            if (definition != null)
            {
                var formatted = definition.Formatter(request.Data);
                title = formatted.Title;
                body = formatted.Body;
                defaultActionUrl = formatted.DefaultActionUrl;
            }
            else
            {
                title = request.Data?.TryGetValue("title", out var t) == true ? t?.ToString() ?? "Notification" : "Notification";
                body = request.Data?.TryGetValue("body", out var b) == true ? b?.ToString() ?? "" : "";
            }

            var actionUrl = request.ActionUrl ?? defaultActionUrl;

            // 3. Evaluate user preferences
            var channels = definition?.DefaultChannels ?? new List<NotificationChannel> { NotificationChannel.InApp };

            var userPreferences = await _context.NotificationPreferences
                .AsNoTracking()
                .Where(p => p.UserId == request.UserId && p.Category == category)
                .ToListAsync(ct);

            var activeChannels = new List<NotificationChannel>();
            foreach (var ch in channels)
            {
                // InApp is always enabled for critical or mandatory types
                if (ch == NotificationChannel.InApp && (definition?.IsMandatory == true || priority == NotificationPriority.Critical))
                {
                    activeChannels.Add(ch);
                    continue;
                }

                // Mandatory email for critical security/account events
                if (ch == NotificationChannel.Email && definition?.IsMandatory == true)
                {
                    activeChannels.Add(ch);
                    continue;
                }

                var pref = userPreferences.FirstOrDefault(p => p.Channel == ch);
                if (pref == null || pref.Enabled)
                {
                    activeChannels.Add(ch);
                }
            }

            // Always at least InApp if all channels were disabled
            if (!activeChannels.Contains(NotificationChannel.InApp))
            {
                activeChannels.Add(NotificationChannel.InApp);
            }

            // 4. Create Notification entity
            var notification = new Notification
            {
                UserId = request.UserId,
                Type = request.Type,
                Category = category,
                Priority = priority,
                Title = title,
                Body = body,
                ActionUrl = actionUrl,
                EntityType = request.EntityType,
                EntityId = request.EntityId,
                DataJson = request.Data != null ? JsonSerializer.Serialize(request.Data) : null,
                DedupeKey = request.DedupeKey,
                CreatedAt = DateTime.UtcNow
            };

            // 5. Create Deliveries
            foreach (var ch in activeChannels)
            {
                notification.Deliveries.Add(new NotificationDelivery
                {
                    Channel = ch,
                    Status = DeliveryStatus.Pending,
                    AttemptCount = 0,
                    CreatedAt = DateTime.UtcNow
                });
            }

            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync(ct);

            // 6. Signal the background dispatcher
            await _queue.QueueNotificationAsync(notification.NotificationId, ct);

            return notification;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish notification {Type} for User {UserId}", request.Type, request.UserId);
            throw;
        }
    }

    public async Task<int> PublishToRoleAsync(UserType role, NotificationPublishRequest request, CancellationToken ct = default)
    {
        var users = await _context.Users
            .AsNoTracking()
            .Where(u => u.UserType == role && u.AccountStatus == "Active")
            .Select(u => u.UserId)
            .ToListAsync(ct);

        var count = 0;
        foreach (var userId in users)
        {
            var req = new NotificationPublishRequest(
                userId,
                request.Type,
                request.Data,
                request.EntityType,
                request.EntityId,
                request.ActionUrl,
                !string.IsNullOrEmpty(request.DedupeKey) ? $"{request.DedupeKey}:user:{userId}" : null,
                request.PriorityOverride
            );

            await PublishAsync(req, ct);
            count++;
        }

        return count;
    }

    public async Task<int> PublishToAllAsync(NotificationPublishRequest request, CancellationToken ct = default)
    {
        var users = await _context.Users
            .AsNoTracking()
            .Where(u => u.AccountStatus == "Active")
            .Select(u => u.UserId)
            .ToListAsync(ct);

        var count = 0;
        foreach (var userId in users)
        {
            var req = new NotificationPublishRequest(
                userId,
                request.Type,
                request.Data,
                request.EntityType,
                request.EntityId,
                request.ActionUrl,
                !string.IsNullOrEmpty(request.DedupeKey) ? $"{request.DedupeKey}:user:{userId}" : null,
                request.PriorityOverride
            );

            await PublishAsync(req, ct);
            count++;
        }

        return count;
    }

    public async Task<NotificationInboxResponse> GetInboxAsync(int userId, NotificationQuery query)
    {
        var limit = Math.Clamp(query.Limit, 1, 50);

        var q = _context.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId && n.ArchivedAt == null);

        if (string.Equals(query.Status, "unread", StringComparison.OrdinalIgnoreCase))
        {
            q = q.Where(n => n.ReadAt == null);
        }

        if (!string.IsNullOrWhiteSpace(query.Category) &&
            Enum.TryParse<NotificationCategory>(query.Category, true, out var cat))
        {
            q = q.Where(n => n.Category == cat);
        }

        if (query.Cursor.HasValue)
        {
            q = q.Where(n => n.NotificationId < query.Cursor.Value);
        }

        var rawItems = await q
            .OrderByDescending(n => n.NotificationId)
            .Take(limit + 1)
            .ToListAsync();

        var hasMore = rawItems.Count > limit;
        var page = rawItems.Take(limit).ToList();
        var nextCursor = hasMore ? page.LastOrDefault()?.NotificationId : null;

        var items = page.Select(MapToDto).ToList();

        var totalUnread = await _context.Notifications
            .CountAsync(n => n.UserId == userId && n.ReadAt == null && n.ArchivedAt == null);

        return new NotificationInboxResponse
        {
            Items = items,
            NextCursor = nextCursor,
            HasMore = hasMore,
            TotalUnread = totalUnread
        };
    }

    public async Task<UnreadCountDto> GetUnreadCountAsync(int userId)
    {
        var unreadItems = await _context.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId && n.ReadAt == null && n.ArchivedAt == null)
            .Select(n => n.Category)
            .ToListAsync();

        var byCat = unreadItems
            .GroupBy(c => c.ToString())
            .ToDictionary(g => g.Key, g => g.Count());

        return new UnreadCountDto
        {
            Total = unreadItems.Count,
            ByCategory = byCat
        };
    }

    public async Task<int> MarkReadAsync(int userId, IEnumerable<long> ids)
    {
        var idList = ids.ToList();
        if (idList.Count == 0) return 0;

        var now = DateTime.UtcNow;
        var notifications = await _context.Notifications
            .Where(n => n.UserId == userId && idList.Contains(n.NotificationId) && n.ReadAt == null)
            .ToListAsync();

        if (notifications.Count == 0) return 0;

        foreach (var n in notifications)
        {
            n.ReadAt = now;
        }

        await _context.SaveChangesAsync();

        // Broadcast to user's devices
        var unreadCount = await GetUnreadCountAsync(userId);
        await _hubContext.Clients.Group($"User-{userId}").SendAsync("notification.read", new
        {
            ids = notifications.Select(n => n.NotificationId).ToArray(),
            unreadCount
        });

        return notifications.Count;
    }

    public async Task<int> MarkAllReadAsync(int userId, string? category = null)
    {
        var now = DateTime.UtcNow;
        var q = _context.Notifications
            .Where(n => n.UserId == userId && n.ReadAt == null && n.ArchivedAt == null);

        if (!string.IsNullOrWhiteSpace(category) &&
            Enum.TryParse<NotificationCategory>(category, true, out var cat))
        {
            q = q.Where(n => n.Category == cat);
        }

        var notifications = await q.ToListAsync();
        if (notifications.Count == 0) return 0;

        foreach (var n in notifications)
        {
            n.ReadAt = now;
        }

        await _context.SaveChangesAsync();

        var unreadCount = await GetUnreadCountAsync(userId);
        await _hubContext.Clients.Group($"User-{userId}").SendAsync("notification.read", new
        {
            all = true,
            category,
            unreadCount
        });

        return notifications.Count;
    }

    public async Task<bool> ArchiveAsync(int userId, long notificationId)
    {
        var notification = await _context.Notifications
            .FirstOrDefaultAsync(n => n.UserId == userId && n.NotificationId == notificationId);

        if (notification == null) return false;

        notification.ArchivedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var unreadCount = await GetUnreadCountAsync(userId);
        await _hubContext.Clients.Group($"User-{userId}").SendAsync("notification.unreadCount", unreadCount);

        return true;
    }

    public async Task<NotificationPreferencesResponse> GetPreferencesAsync(int userId)
    {
        var storedPrefs = await _context.NotificationPreferences
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .ToListAsync();

        var categories = Enum.GetValues<NotificationCategory>();
        var channels = new[] { NotificationChannel.InApp, NotificationChannel.WebPush, NotificationChannel.Email };

        var list = new List<NotificationPreferenceItemDto>();

        foreach (var cat in categories)
        {
            foreach (var ch in channels)
            {
                var isLocked = false;
                // Critical account security / email is locked
                if (cat == NotificationCategory.Account && ch == NotificationChannel.Email)
                {
                    isLocked = true;
                }

                var existing = storedPrefs.FirstOrDefault(p => p.Category == cat && p.Channel == ch);
                var enabled = existing?.Enabled ?? true;

                list.Add(new NotificationPreferenceItemDto
                {
                    Category = cat.ToString(),
                    Channel = ch.ToString(),
                    Enabled = isLocked || enabled,
                    IsLocked = isLocked
                });
            }
        }

        return new NotificationPreferencesResponse { Preferences = list };
    }

    public async Task UpdatePreferencesAsync(int userId, UpdateNotificationPreferencesRequest request)
    {
        var existingPrefs = await _context.NotificationPreferences
            .Where(p => p.UserId == userId)
            .ToListAsync();

        var now = DateTime.UtcNow;

        foreach (var item in request.Preferences)
        {
            if (!Enum.TryParse<NotificationCategory>(item.Category, true, out var cat) ||
                !Enum.TryParse<NotificationChannel>(item.Channel, true, out var ch))
            {
                continue;
            }

            // Locked preferences cannot be altered
            if (cat == NotificationCategory.Account && ch == NotificationChannel.Email)
            {
                continue;
            }

            var pref = existingPrefs.FirstOrDefault(p => p.Category == cat && p.Channel == ch);
            if (pref != null)
            {
                pref.Enabled = item.Enabled;
                pref.UpdatedAt = now;
            }
            else
            {
                _context.NotificationPreferences.Add(new NotificationPreference
                {
                    UserId = userId,
                    Category = cat,
                    Channel = ch,
                    Enabled = item.Enabled,
                    UpdatedAt = now
                });
            }
        }

        await _context.SaveChangesAsync();
    }

    public async Task RegisterPushSubscriptionAsync(int userId, PushSubscriptionRequestDto request)
    {
        var endpoint = request.Endpoint.Trim();
        var existing = await _context.PushSubscriptions
            .FirstOrDefaultAsync(s => s.Endpoint == endpoint);

        if (existing != null)
        {
            existing.UserId = userId;
            existing.P256dh = request.Keys.P256dh;
            existing.Auth = request.Keys.Auth;
            existing.UserAgent = request.UserAgent;
            existing.LastUsedAt = DateTime.UtcNow;
        }
        else
        {
            _context.PushSubscriptions.Add(new PushSubscription
            {
                UserId = userId,
                Endpoint = endpoint,
                P256dh = request.Keys.P256dh,
                Auth = request.Keys.Auth,
                UserAgent = request.UserAgent,
                CreatedAt = DateTime.UtcNow,
                LastUsedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();
    }

    public async Task<bool> UnregisterPushSubscriptionAsync(int userId, string endpoint)
    {
        var sub = await _context.PushSubscriptions
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Endpoint == endpoint.Trim());

        if (sub == null) return false;

        _context.PushSubscriptions.Remove(sub);
        await _context.SaveChangesAsync();
        return true;
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
