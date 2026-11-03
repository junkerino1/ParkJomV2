namespace ParkJomV2.Models.Enums;

public enum NotificationCategory
{
    Booking = 1,
    Wallet = 2,
    Listing = 3,
    Support = 4,
    System = 5,
    Account = 6
}

public enum NotificationPriority
{
    Low = 1,
    Normal = 2,
    High = 3,
    Critical = 4
}

public enum DeliveryStatus
{
    Pending = 1,
    Sent = 2,
    Failed = 3,
    Skipped = 4
}
