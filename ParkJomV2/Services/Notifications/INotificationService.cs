using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.Services.Notifications;

public interface INotificationService
{
    Task<Notification?> PublishAsync(NotificationPublishRequest request, CancellationToken ct = default);
    Task<int> PublishToRoleAsync(UserType role, NotificationPublishRequest request, CancellationToken ct = default);
    Task<int> PublishToAllAsync(NotificationPublishRequest request, CancellationToken ct = default);

    Task<NotificationInboxResponse> GetInboxAsync(int userId, NotificationQuery query);
    Task<UnreadCountDto> GetUnreadCountAsync(int userId);
    Task<int> MarkReadAsync(int userId, IEnumerable<long> ids);
    Task<int> MarkAllReadAsync(int userId, string? category = null);
    Task<bool> ArchiveAsync(int userId, long notificationId);

    Task<NotificationPreferencesResponse> GetPreferencesAsync(int userId);
    Task UpdatePreferencesAsync(int userId, UpdateNotificationPreferencesRequest request);

    Task RegisterPushSubscriptionAsync(int userId, PushSubscriptionRequestDto request);
    Task<bool> UnregisterPushSubscriptionAsync(int userId, string endpoint);
}
