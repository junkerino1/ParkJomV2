using ParkJomV2.Models.Enums;

namespace ParkJomV2.Services.Notifications;

public class NotificationDefinition
{
    public string Type { get; set; } = string.Empty;
    public NotificationCategory Category { get; set; }
    public NotificationPriority DefaultPriority { get; set; } = NotificationPriority.Normal;
    public List<NotificationChannel> DefaultChannels { get; set; } = new();
    public bool IsMandatory { get; set; } // When true, users cannot completely disable this notification category
    public Func<Dictionary<string, object?>?, (string Title, string Body, string? DefaultActionUrl)> Formatter { get; set; } = null!;
}

public static class NotificationCatalog
{
    private static readonly Dictionary<string, NotificationDefinition> Definitions = new(StringComparer.OrdinalIgnoreCase);

    static NotificationCatalog()
    {
        Register(new NotificationDefinition
        {
            Type = "booking.confirmed",
            Category = NotificationCategory.Booking,
            DefaultPriority = NotificationPriority.Normal,
            DefaultChannels = new() { NotificationChannel.InApp, NotificationChannel.WebPush, NotificationChannel.Email },
            Formatter = data =>
            {
                var refCode = GetString(data, "bookingReference", "Booking");
                var spotName = GetString(data, "spotName", "Parking spot");
                var startDate = GetString(data, "startDate", "");
                var bookingId = GetString(data, "bookingId", "");
                return (
                    Title: $"Booking Confirmed ({refCode})",
                    Body: $"Your booking for {spotName} starting {startDate} is confirmed and reserved.",
                    DefaultActionUrl: !string.IsNullOrEmpty(bookingId) ? $"/commuter/bookings/{bookingId}" : "/commuter/bookings"
                );
            }
        });

        Register(new NotificationDefinition
        {
            Type = "booking.received",
            Category = NotificationCategory.Booking,
            DefaultPriority = NotificationPriority.Normal,
            DefaultChannels = new() { NotificationChannel.InApp, NotificationChannel.WebPush },
            Formatter = data =>
            {
                var refCode = GetString(data, "bookingReference", "New booking");
                var spotName = GetString(data, "spotName", "your spot");
                var dateRange = GetString(data, "dateRange", "");
                var bookingId = GetString(data, "bookingId", "");
                return (
                    Title: $"New Booking: {spotName}",
                    Body: $"Booking {refCode} received for {dateRange}.",
                    DefaultActionUrl: !string.IsNullOrEmpty(bookingId) ? $"/owner/bookings/{bookingId}" : "/owner/bookings"
                );
            }
        });

        Register(new NotificationDefinition
        {
            Type = "booking.starting_soon",
            Category = NotificationCategory.Booking,
            DefaultPriority = NotificationPriority.High,
            DefaultChannels = new() { NotificationChannel.InApp, NotificationChannel.WebPush },
            Formatter = data =>
            {
                var spotName = GetString(data, "spotName", "Parking spot");
                var mins = GetInt(data, "minutesBefore", 15);
                var bookingId = GetString(data, "bookingId", "");
                return (
                    Title: $"Parking starts in {mins} minutes",
                    Body: $"{spotName} is ready for check-in. Drive safely!",
                    DefaultActionUrl: !string.IsNullOrEmpty(bookingId) ? $"/commuter/bookings/{bookingId}" : "/commuter/active-booking"
                );
            }
        });

        Register(new NotificationDefinition
        {
            Type = "booking.ending_soon",
            Category = NotificationCategory.Booking,
            DefaultPriority = NotificationPriority.High,
            DefaultChannels = new() { NotificationChannel.InApp, NotificationChannel.WebPush },
            Formatter = data =>
            {
                var spotName = GetString(data, "spotName", "Parking spot");
                var mins = GetInt(data, "minutesBefore", 30);
                var bookingId = GetString(data, "bookingId", "");
                return (
                    Title: $"Parking ends in {mins} minutes",
                    Body: $"Please return to {spotName} before your booking expires to avoid overstay charges.",
                    DefaultActionUrl: !string.IsNullOrEmpty(bookingId) ? $"/commuter/bookings/{bookingId}" : "/commuter/active-booking"
                );
            }
        });

        Register(new NotificationDefinition
        {
            Type = "booking.overstay_charged",
            Category = NotificationCategory.Booking,
            DefaultPriority = NotificationPriority.High,
            DefaultChannels = new() { NotificationChannel.InApp, NotificationChannel.Email, NotificationChannel.WebPush },
            Formatter = data =>
            {
                var penalty = GetString(data, "penaltyAmount", "RM 0.00");
                var hours = GetInt(data, "overstayHours", 1);
                var bookingId = GetString(data, "bookingId", "");
                return (
                    Title: "Overstay Penalty Applied",
                    Body: $"An overstay penalty of {penalty} ({hours} hr(s)) has been charged to your wallet.",
                    DefaultActionUrl: !string.IsNullOrEmpty(bookingId) ? $"/commuter/bookings/{bookingId}" : "/commuter/wallet"
                );
            }
        });

        Register(new NotificationDefinition
        {
            Type = "booking.overstay_warning",
            Category = NotificationCategory.Booking,
            DefaultPriority = NotificationPriority.High,
            DefaultChannels = new() { NotificationChannel.InApp, NotificationChannel.WebPush },
            Formatter = data =>
            {
                var spotName = GetString(data, "spotName", "your parking spot");
                var windowEnd = GetString(data, "windowEnd", "the scheduled closing time");
                var bookingId = GetString(data, "bookingId", "");
                return (
                    Title: "Parking time exceeded",
                    Body: $"Today's parking window at {spotName} ended at {windowEnd}. The 15-minute grace period has passed; overstay fees are RM5 per started hour, up to RM30 for this daily window. Please leave now.",
                    DefaultActionUrl: !string.IsNullOrEmpty(bookingId) ? $"/commuter/bookings/{bookingId}" : "/commuter/bookings"
                );
            }
        });

        Register(new NotificationDefinition
        {
            Type = "booking.overstay_payment_due",
            Category = NotificationCategory.Booking,
            DefaultPriority = NotificationPriority.High,
            DefaultChannels = new() { NotificationChannel.InApp, NotificationChannel.WebPush },
            Formatter = data =>
            {
                var penalty = GetString(data, "penaltyAmount", "RM 0.00");
                var bookingId = GetString(data, "bookingId", "");
                return (
                    Title: "Overstay payment due",
                    Body: $"Your overstay penalty of {penalty} could not be deducted from your wallet. Add funds to settle it before your next bollard unlock.",
                    DefaultActionUrl: !string.IsNullOrEmpty(bookingId) ? $"/commuter/bookings/{bookingId}" : "/commuter/wallet"
                );
            }
        });

        Register(new NotificationDefinition
        {
            Type = "booking.cancelled",
            Category = NotificationCategory.Booking,
            DefaultPriority = NotificationPriority.Normal,
            DefaultChannels = new() { NotificationChannel.InApp, NotificationChannel.Email },
            Formatter = data =>
            {
                var refCode = GetString(data, "bookingReference", "Booking");
                var reason = GetString(data, "reason", "Cancelled by user");
                var refund = GetString(data, "refundAmount", null);
                var body = string.IsNullOrEmpty(refund)
                    ? $"Booking {refCode} was cancelled. Reason: {reason}"
                    : $"Booking {refCode} was cancelled. Refund of {refund} credited to wallet.";
                return (
                    Title: $"Booking Cancelled ({refCode})",
                    Body: body,
                    DefaultActionUrl: "/commuter/bookings"
                );
            }
        });

        Register(new NotificationDefinition
        {
            Type = "booking.completed",
            Category = NotificationCategory.Booking,
            DefaultPriority = NotificationPriority.Low,
            DefaultChannels = new() { NotificationChannel.InApp, NotificationChannel.Email },
            Formatter = data =>
            {
                var spotName = GetString(data, "spotName", "Parking spot");
                var bookingId = GetString(data, "bookingId", "");
                return (
                    Title: "Booking Completed",
                    Body: $"Thank you for parking at {spotName}. Leave a review to help other commuters!",
                    DefaultActionUrl: !string.IsNullOrEmpty(bookingId) ? $"/commuter/bookings/{bookingId}" : "/commuter/bookings"
                );
            }
        });

        Register(new NotificationDefinition
        {
            Type = "wallet.topup_succeeded",
            Category = NotificationCategory.Wallet,
            DefaultPriority = NotificationPriority.Normal,
            DefaultChannels = new() { NotificationChannel.InApp, NotificationChannel.Email },
            Formatter = data =>
            {
                var amount = GetString(data, "amount", "RM 0.00");
                var newBalance = GetString(data, "newBalance", "");
                return (
                    Title: "Top-Up Successful",
                    Body: $"Successfully added {amount} to your ParkJom wallet. Balance: {newBalance}",
                    DefaultActionUrl: "/commuter/wallet"
                );
            }
        });

        Register(new NotificationDefinition
        {
            Type = "wallet.topup_failed",
            Category = NotificationCategory.Wallet,
            DefaultPriority = NotificationPriority.Normal,
            DefaultChannels = new() { NotificationChannel.InApp },
            Formatter = data =>
            {
                var reason = GetString(data, "reason", "Payment failed or was cancelled.");
                return (
                    Title: "Top-Up Unsuccessful",
                    Body: $"Your wallet top-up could not be processed: {reason}",
                    DefaultActionUrl: "/commuter/wallet"
                );
            }
        });

        Register(new NotificationDefinition
        {
            Type = "wallet.low_balance",
            Category = NotificationCategory.Wallet,
            DefaultPriority = NotificationPriority.Low,
            DefaultChannels = new() { NotificationChannel.InApp },
            Formatter = data =>
            {
                var balance = GetString(data, "currentBalance", "RM 0.00");
                return (
                    Title: "Low Wallet Balance",
                    Body: $"Your balance is {balance}. Top up soon to ensure seamless future bookings.",
                    DefaultActionUrl: "/commuter/wallet"
                );
            }
        });

        Register(new NotificationDefinition
        {
            Type = "payout.released",
            Category = NotificationCategory.Wallet,
            DefaultPriority = NotificationPriority.Normal,
            DefaultChannels = new() { NotificationChannel.InApp, NotificationChannel.Email },
            Formatter = data =>
            {
                var amount = GetString(data, "amount", "RM 0.00");
                return (
                    Title: "Payout Released",
                    Body: $"{amount} has been released from on-hold into your available balance.",
                    DefaultActionUrl: "/owner/earnings"
                );
            }
        });

        Register(new NotificationDefinition
        {
            Type = "withdrawal.status_changed",
            Category = NotificationCategory.Wallet,
            DefaultPriority = NotificationPriority.Normal,
            DefaultChannels = new() { NotificationChannel.InApp, NotificationChannel.Email },
            Formatter = data =>
            {
                var status = GetString(data, "status", "Processed") ?? "Processed";
                var amount = GetString(data, "amount", "RM 0.00") ?? "RM 0.00";
                return (
                    Title: $"Withdrawal {status}",
                    Body: $"Your bank withdrawal of {amount} is now {status.ToLower()}.",
                    DefaultActionUrl: "/owner/earnings"
                );
            }
        });

        Register(new NotificationDefinition
        {
            Type = "verification.submitted",
            Category = NotificationCategory.Listing,
            DefaultPriority = NotificationPriority.Normal,
            DefaultChannels = new() { NotificationChannel.InApp },
            Formatter = data =>
            {
                var ownerName = GetString(data, "ownerName", "An owner");
                var propertyName = GetString(data, "propertyName", "property");
                var reqId = GetString(data, "requestId", "");
                return (
                    Title: "New Verification Request",
                    Body: $"{ownerName} submitted verification documents for {propertyName}.",
                    DefaultActionUrl: !string.IsNullOrEmpty(reqId) ? $"/admin/verifications/{reqId}" : "/admin/verifications"
                );
            }
        });

        Register(new NotificationDefinition
        {
            Type = "verification.approved",
            Category = NotificationCategory.Listing,
            DefaultPriority = NotificationPriority.High,
            DefaultChannels = new() { NotificationChannel.InApp, NotificationChannel.Email, NotificationChannel.WebPush },
            Formatter = data =>
            {
                var propertyName = GetString(data, "propertyName", "Your property");
                var spotId = GetString(data, "spotId", "");
                return (
                    Title: "Property Verification Approved!",
                    Body: $"{propertyName} has been verified! You can now configure and publish your parking spot.",
                    DefaultActionUrl: !string.IsNullOrEmpty(spotId) ? $"/owner/spots/{spotId}/edit" : "/owner/spots"
                );
            }
        });

        Register(new NotificationDefinition
        {
            Type = "verification.rejected",
            Category = NotificationCategory.Listing,
            DefaultPriority = NotificationPriority.High,
            DefaultChannels = new() { NotificationChannel.InApp, NotificationChannel.Email },
            Formatter = data =>
            {
                var propertyName = GetString(data, "propertyName", "Your property");
                var notes = GetString(data, "reviewNotes", "Document could not be verified.");
                var reqId = GetString(data, "requestId", "");
                return (
                    Title: "Verification Needs Attention",
                    Body: $"{propertyName} verification was rejected: {notes}",
                    DefaultActionUrl: !string.IsNullOrEmpty(reqId) ? $"/owner/verifications/{reqId}" : "/owner/spots"
                );
            }
        });

        Register(new NotificationDefinition
        {
            Type = "listing.suspended",
            Category = NotificationCategory.Listing,
            DefaultPriority = NotificationPriority.High,
            DefaultChannels = new() { NotificationChannel.InApp, NotificationChannel.Email },
            Formatter = data =>
            {
                var spotName = GetString(data, "spotName", "Parking spot");
                var reason = GetString(data, "reason", "Platform policy review");
                return (
                    Title: "Listing Suspended",
                    Body: $"{spotName} was suspended: {reason}",
                    DefaultActionUrl: "/owner/spots"
                );
            }
        });

        Register(new NotificationDefinition
        {
            Type = "listing.reinstated",
            Category = NotificationCategory.Listing,
            DefaultPriority = NotificationPriority.Normal,
            DefaultChannels = new() { NotificationChannel.InApp, NotificationChannel.Email },
            Formatter = data =>
            {
                var spotName = GetString(data, "spotName", "Parking spot");
                return (
                    Title: "Listing Reinstated",
                    Body: $"{spotName} is now active and bookable again.",
                    DefaultActionUrl: "/owner/spots"
                );
            }
        });

        Register(new NotificationDefinition
        {
            Type = "review.received",
            Category = NotificationCategory.Listing,
            DefaultPriority = NotificationPriority.Low,
            DefaultChannels = new() { NotificationChannel.InApp },
            Formatter = data =>
            {
                var rating = GetInt(data, "rating", 5);
                var spotName = GetString(data, "spotName", "your spot");
                return (
                    Title: $"New Review ({rating}★)",
                    Body: $"A commuter left a {rating}-star review for {spotName}.",
                    DefaultActionUrl: "/owner/reviews"
                );
            }
        });

        Register(new NotificationDefinition
        {
            Type = "review.replied",
            Category = NotificationCategory.Booking,
            DefaultPriority = NotificationPriority.Low,
            DefaultChannels = new() { NotificationChannel.InApp },
            Formatter = data =>
            {
                var spotName = GetString(data, "spotName", "the parking spot");
                return (
                    Title: "Host Replied to Review",
                    Body: $"The owner of {spotName} replied to your review.",
                    DefaultActionUrl: "/commuter/reviews"
                );
            }
        });

        Register(new NotificationDefinition
        {
            Type = "iot.device_offline",
            Category = NotificationCategory.Support,
            DefaultPriority = NotificationPriority.High,
            DefaultChannels = new() { NotificationChannel.InApp, NotificationChannel.WebPush },
            Formatter = data =>
            {
                var deviceSerial = GetString(data, "serialNumber", "Smart Bollard");
                var spotName = GetString(data, "spotName", "Spot");
                return (
                    Title: "Smart Bollard Offline",
                    Body: $"{deviceSerial} at {spotName} missed heartbeats and is currently offline.",
                    DefaultActionUrl: "/owner/iot-devices"
                );
            }
        });

        Register(new NotificationDefinition
        {
            Type = "support.ticket_updated",
            Category = NotificationCategory.Support,
            DefaultPriority = NotificationPriority.Normal,
            DefaultChannels = new() { NotificationChannel.InApp, NotificationChannel.WebPush },
            Formatter = data =>
            {
                var ticketRef = GetString(data, "ticketReference", "Ticket");
                var status = GetString(data, "status", "Updated");
                var ticketId = GetString(data, "ticketId", "");
                return (
                    Title: $"Support Ticket {status} ({ticketRef})",
                    Body: $"Your ticket {ticketRef} status has changed to {status}.",
                    DefaultActionUrl: !string.IsNullOrEmpty(ticketId) ? $"/support/tickets/{ticketId}" : "/support"
                );
            }
        });

        Register(new NotificationDefinition
        {
            Type = "support.message_received",
            Category = NotificationCategory.Support,
            DefaultPriority = NotificationPriority.Normal,
            DefaultChannels = new() { NotificationChannel.InApp, NotificationChannel.WebPush },
            Formatter = data =>
            {
                var ticketRef = GetString(data, "ticketReference", "Ticket");
                var senderName = GetString(data, "senderName", "Support agent");
                var ticketId = GetString(data, "ticketId", "");
                return (
                    Title: $"New Support Reply ({ticketRef})",
                    Body: $"{senderName} replied to your support request.",
                    DefaultActionUrl: !string.IsNullOrEmpty(ticketId) ? $"/support/tickets/{ticketId}" : "/support"
                );
            }
        });

        Register(new NotificationDefinition
        {
            Type = "account.status_changed",
            Category = NotificationCategory.Account,
            DefaultPriority = NotificationPriority.Critical,
            DefaultChannels = new() { NotificationChannel.InApp, NotificationChannel.Email },
            IsMandatory = true,
            Formatter = data =>
            {
                var newStatus = GetString(data, "newStatus", "Updated");
                var reason = GetString(data, "reason", "");
                var body = string.IsNullOrEmpty(reason)
                    ? $"Your ParkJom account status is now {newStatus}."
                    : $"Your ParkJom account status is now {newStatus}. Reason: {reason}";
                return (
                    Title: $"Account Status: {newStatus}",
                    Body: body,
                    DefaultActionUrl: "/profile"
                );
            }
        });

        Register(new NotificationDefinition
        {
            Type = "system.announcement",
            Category = NotificationCategory.System,
            DefaultPriority = NotificationPriority.Normal,
            DefaultChannels = new() { NotificationChannel.InApp },
            Formatter = data =>
            {
                var title = GetString(data, "title", "System Announcement") ?? "System Announcement";
                var body = GetString(data, "body", "Notice from ParkJom platform administrators.") ?? "Notice from ParkJom platform administrators.";
                var actionUrl = GetString(data, "actionUrl", null);
                return (Title: title, Body: body, DefaultActionUrl: actionUrl);
            }
        });
    }

    private static void Register(NotificationDefinition definition)
    {
        Definitions[definition.Type] = definition;
    }

    public static NotificationDefinition? Get(string type)
    {
        Definitions.TryGetValue(type, out var def);
        return def;
    }

    public static IReadOnlyCollection<NotificationDefinition> GetAll() => Definitions.Values;

    private static string? GetString(Dictionary<string, object?>? data, string key, string? fallback)
    {
        if (data != null && data.TryGetValue(key, out var val) && val != null)
        {
            return val.ToString() ?? fallback;
        }
        return fallback;
    }

    private static int GetInt(Dictionary<string, object?>? data, string key, int fallback)
    {
        if (data != null && data.TryGetValue(key, out var val) && val != null)
        {
            if (val is int i) return i;
            if (int.TryParse(val.ToString(), out var parsed)) return parsed;
        }
        return fallback;
    }
}
