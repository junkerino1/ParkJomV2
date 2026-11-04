using System.Threading.Channels;

namespace ParkJomV2.Services.Notifications;

public interface INotificationQueue
{
    ValueTask QueueNotificationAsync(long notificationId, CancellationToken ct = default);
    ValueTask<long> DequeueAsync(CancellationToken ct = default);
}

public class NotificationQueue : INotificationQueue
{
    private readonly Channel<long> _channel;

    public NotificationQueue()
    {
        // Unbounded channel for notification IDs to ensure publishes never block
        _channel = Channel.CreateUnbounded<long>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    public ValueTask QueueNotificationAsync(long notificationId, CancellationToken ct = default)
    {
        return _channel.Writer.WriteAsync(notificationId, ct);
    }

    public ValueTask<long> DequeueAsync(CancellationToken ct = default)
    {
        return _channel.Reader.ReadAsync(ct);
    }
}
