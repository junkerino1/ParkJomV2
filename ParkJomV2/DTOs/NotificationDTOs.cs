using System.ComponentModel.DataAnnotations;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.DTOs;

public class NotificationDto
{
    public long Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? ActionUrl { get; set; }
    public NotificationEntityDto? Entity { get; set; }
    public Dictionary<string, object?>? Data { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
}

public class NotificationEntityDto
{
    public string? Type { get; set; }
    public string? Id { get; set; }
}

public class NotificationQuery
{
    public long? Cursor { get; set; }
    public int Limit { get; set; } = 20;
    public string Status { get; set; } = "all"; // all, unread
    public string? Category { get; set; }
}

public class NotificationInboxResponse
{
    public List<NotificationDto> Items { get; set; } = new();
    public long? NextCursor { get; set; }
    public bool HasMore { get; set; }
    public int TotalUnread { get; set; }
}

public class UnreadCountDto
{
    public int Total { get; set; }
    public Dictionary<string, int> ByCategory { get; set; } = new();
}

public class MarkReadRequestDto
{
    public List<long>? Ids { get; set; }
}

public class NotificationPreferenceItemDto
{
    public string Category { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public bool IsLocked { get; set; }
}

public class NotificationPreferencesResponse
{
    public List<NotificationPreferenceItemDto> Preferences { get; set; } = new();
}

public class UpdatePreferenceItemDto
{
    [Required]
    public string Category { get; set; } = string.Empty;

    [Required]
    public string Channel { get; set; } = string.Empty;

    public bool Enabled { get; set; }
}

public class UpdateNotificationPreferencesRequest
{
    [Required]
    public List<UpdatePreferenceItemDto> Preferences { get; set; } = new();
}

public class PushSubscriptionKeysDto
{
    [Required]
    public string P256dh { get; set; } = string.Empty;

    [Required]
    public string Auth { get; set; } = string.Empty;
}

public class PushSubscriptionRequestDto
{
    [Required]
    public string Endpoint { get; set; } = string.Empty;

    [Required]
    public PushSubscriptionKeysDto Keys { get; set; } = new();

    public string? UserAgent { get; set; }
}

public class AdminBroadcastRequestDto
{
    public string? TargetRole { get; set; } // "Renter", "PropertyOwner", "Admin", or null for all
    public string? Type { get; set; } = "system.announcement";

    [Required]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    public string Body { get; set; } = string.Empty;

    [StringLength(500)]
    public string? ActionUrl { get; set; }

    public string? Priority { get; set; } = "Normal";
}

public class DeliveryStatusDto
{
    public long DeliveryId { get; set; }
    public long NotificationId { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public string? LastError { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class NotificationPublishRequest
{
    public int UserId { get; set; }
    public string Type { get; set; } = string.Empty;
    public Dictionary<string, object?>? Data { get; set; }
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? ActionUrl { get; set; }
    public string? DedupeKey { get; set; }
    public NotificationPriority? PriorityOverride { get; set; }

    public NotificationPublishRequest() { }

    public NotificationPublishRequest(
        int userId,
        string type,
        Dictionary<string, object?>? data = null,
        string? entityType = null,
        string? entityId = null,
        string? actionUrl = null,
        string? dedupeKey = null,
        NotificationPriority? priorityOverride = null)
    {
        UserId = userId;
        Type = type;
        Data = data;
        EntityType = entityType;
        EntityId = entityId;
        ActionUrl = actionUrl;
        DedupeKey = dedupeKey;
        PriorityOverride = priorityOverride;
    }
}
