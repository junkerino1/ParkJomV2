using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using ParkJomV2.Data;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;

namespace ParkJomV2.Services;

/// <summary>Maintains the backend's MQTT connection and records inbound device messages.</summary>
public sealed class MqttService : BackgroundService, IMqttService
{
    private readonly IMqttClient _client;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MqttService> _logger;
    private readonly string _host;
    private readonly int _port;
    private readonly string _clientId;
    private readonly string _topicRoot;
    private readonly string? _username;
    private readonly string? _password;
    private readonly ConcurrentDictionary<int, int> _pendingAutoLocks = new();

    public MqttService(
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory,
        ILogger<MqttService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _host = configuration["Mqtt:Broker:Host"] ?? "localhost";
        _port = int.TryParse(configuration["Mqtt:Broker:Port"], out var port) ? port : 1883;
        _clientId = configuration["Mqtt:ClientId"] ?? "parkjom-backend";
        _topicRoot = (configuration["Mqtt:TopicRoot"] ?? "parkjom/devices").TrimEnd('/');
        _username = configuration["Mqtt:Broker:Username"];
        _password = configuration["Mqtt:Broker:Password"];

        _client = new MqttFactory().CreateMqttClient();
        _client.ApplicationMessageReceivedAsync += HandleMessageAsync;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var optionsBuilder = new MqttClientOptionsBuilder()
            .WithTcpServer(_host, _port)
            .WithClientId(_clientId)
            .WithCleanSession();
        if (!string.IsNullOrWhiteSpace(_username))
        {
            optionsBuilder.WithCredentials(_username, _password);
        }

        var options = optionsBuilder.Build();
        // Wildcard subscriptions receive acknowledgements and telemetry from every UUID topic.
        var subscription = new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(filter => filter
                .WithTopic($"{_topicRoot}/+/ack")
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
            .WithTopicFilter(filter => filter
                .WithTopic($"{_topicRoot}/+/telemetry")
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
            .Build();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_client.IsConnected)
                {
                    await _client.ConnectAsync(options, stoppingToken);
                    await _client.SubscribeAsync(subscription, stoppingToken);
                    _logger.LogInformation("Connected to MQTT broker {Host}:{Port} and subscribed to device ACK and telemetry topics", _host, _port);
                }

                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MQTT connection or subscription failed; retrying in five seconds");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    public async Task PublishCommandAsync(string deviceUuid, string command, CancellationToken cancellationToken = default)
    {
        var topic = $"{_topicRoot}/{deviceUuid}/commands";
        if (!_client.IsConnected)
        {
            _logger.LogWarning("Cannot publish MQTT command to {Topic}: broker client is disconnected", topic);
            throw new InvalidOperationException("The MQTT broker is not connected.");
        }

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(command)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

        try
        {
            await _client.PublishAsync(message, cancellationToken);
            _logger.LogInformation("MQTT QoS 1 publication accepted for {Topic}", topic);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MQTT publication failed for {Topic}", topic);
            throw new InvalidOperationException("The MQTT command could not be published.", ex);
        }
    }

    private async Task HandleMessageAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        try
        {
            var topic = args.ApplicationMessage.Topic;
            var topicPrefix = $"{_topicRoot}/";
            if (!topic.StartsWith(topicPrefix, StringComparison.Ordinal))
            {
                _logger.LogWarning("Ignoring MQTT message on unexpected topic {Topic}", topic);
                return;
            }

            var suffix = topic[topicPrefix.Length..].Split('/');
            if (suffix.Length != 2 || suffix[0].Length == 0 || suffix[1] is not ("ack" or "telemetry"))
            {
                _logger.LogWarning("Ignoring MQTT message on unexpected topic {Topic}", topic);
                return;
            }

            // Decode the ESP32's UTF-8 MQTT payload and persist its exact text without a topic prefix or truncation.
            var payload = Encoding.UTF8.GetString(args.ApplicationMessage.PayloadSegment.ToArray());
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var device = await db.IoTDevices.FirstOrDefaultAsync(d => d.Esp32Serial == suffix[0]);
            if (device is null)
            {
                _logger.LogWarning("Received MQTT message for unregistered device {DeviceSerial}", suffix[0]);
                return;
            }

            var receivedAt = DateTime.UtcNow;
            // Any accepted message on this device's ACK/telemetry topic is current liveness evidence.
            device.DeviceStatus = DeviceStatus.Online;
            device.UpdatedAt = receivedAt;
            db.IoTStatusLogs.Add(new IoTStatusLog
            {
                IoTDeviceId = device.IoTDeviceId,
                DeviceStatus = device.DeviceStatus,
                Topic = topic,
                Message = payload,
                LoggedAt = receivedAt,
                CreatedAt = receivedAt,
                UpdatedAt = receivedAt
            });

            if (suffix[1] == "ack")
            {
                await ProcessDeviceAcknowledgementAsync(db, device, payload, receivedAt);
            }
            else
            {
                await ProcessTelemetryAsync(db, device, topic, payload, receivedAt);
            }

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process an inbound MQTT message");
        }
    }

    private async Task ProcessDeviceAcknowledgementAsync(
        ApplicationDbContext db,
        IoTDevice device,
        string payload,
        DateTime receivedAt)
    {
        string? command;
        string? result;
        int bookingId;
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            command = root.TryGetProperty("command", out var commandElement) ? commandElement.GetString() : null;
            result = root.TryGetProperty("result", out var resultElement) ? resultElement.GetString() : null;
            if (!root.TryGetProperty("bookingId", out var bookingElement) || !bookingElement.TryGetInt32(out bookingId))
            {
                IoTDeviceLogWriter.Add(db, device, "device_ack_validation", "rejected", receivedAt,
                    details: "ACK did not contain a valid bookingId.");
                return;
            }
        }
        catch (JsonException)
        {
            _logger.LogWarning("Ignoring malformed IoT acknowledgement payload for device {DeviceId}", device.Esp32Serial);
            IoTDeviceLogWriter.Add(db, device, "device_ack_parsing", "failed", receivedAt,
                details: "ACK payload was not valid JSON.");
            return;
        }

        IoTDeviceLogWriter.Add(db, device, "device_ack_received", result ?? "missing_result", receivedAt,
            bookingId, $"command={command ?? "unknown"}");

        if (string.Equals(command, "AUTO_LOCK", StringComparison.OrdinalIgnoreCase))
            _pendingAutoLocks.TryRemove(device.IoTDeviceId, out _);

        if (!string.Equals(result, "SUCCESS", StringComparison.OrdinalIgnoreCase))
            return;

        if (!device.ParkingSpotId.HasValue ||
            !string.Equals(command, "UNLOCK", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(command, "AUTO_LOCK", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var booking = await db.Bookings.FirstOrDefaultAsync(b =>
            b.BookingId == bookingId && b.ParkingSpotId == device.ParkingSpotId.Value);
        if (booking is null)
        {
            _logger.LogWarning("IoT ACK references booking {BookingId} that is not assigned to device {DeviceId}", bookingId, device.Esp32Serial);
            IoTDeviceLogWriter.Add(db, device, "device_ack_booking_validation", "rejected", receivedAt,
                bookingId, "Booking does not belong to the device's assigned parking spot.");
            return;
        }

        if (string.Equals(command, "UNLOCK", StringComparison.OrdinalIgnoreCase) &&
            booking.BookingStatus is BookingStatus.Confirmed or BookingStatus.Active)
        {
            // Start a fresh occupancy interval only once the ESP32 confirms physical unlock.
            booking.CheckedInAt = receivedAt;
            booking.ActualExitAt = null;
            booking.BookingStatus = BookingStatus.Active;
            booking.UpdatedAt = receivedAt;
            IoTDeviceLogWriter.Add(db, device, "unlock_ack_booking_checkin", "completed", receivedAt,
                bookingId, "CheckedInAt set from successful ESP32 ACK.");
        }
        else if (string.Equals(command, "AUTO_LOCK", StringComparison.OrdinalIgnoreCase) &&
                 booking.BookingStatus == BookingStatus.Active &&
                 booking.CheckedInAt.HasValue &&
                 !booking.ActualExitAt.HasValue)
        {
            // Set exit time only after the ESP32 ACK confirms it actually locked.
            booking.ActualExitAt = receivedAt;
            booking.UpdatedAt = receivedAt;
            IoTDeviceLogWriter.Add(db, device, "auto_lock_ack_booking_exit", "completed", receivedAt,
                bookingId, "ActualExitAt set from successful ESP32 ACK.");
        }
    }

    private async Task ProcessTelemetryAsync(
        ApplicationDbContext db,
        IoTDevice device,
        string topic,
        string payload,
        DateTime receivedAt)
    {
        if (!device.IsAssigned || !device.ParkingSpotId.HasValue ||
            !TryReadTelemetry(payload, out var distanceCm, out var bollardPosition) ||
            !string.Equals(bollardPosition, "UNLOCKED", StringComparison.OrdinalIgnoreCase) ||
            !distanceCm.HasValue || distanceCm.Value <= 50)
        {
            return;
        }

        var booking = await db.Bookings
            .Where(b => b.ParkingSpotId == device.ParkingSpotId.Value &&
                        b.BookingStatus == BookingStatus.Active &&
                        b.CheckedInAt != null &&
                        b.ActualExitAt == null)
            .OrderByDescending(b => b.CheckedInAt)
            .FirstOrDefaultAsync();
        if (booking?.CheckedInAt is not DateTime checkedInAt)
            return;

        // Require the previous telemetry reading during this check-in to show a nearby vehicle.
        // This prevents the first post-unlock reading from auto-locking while the renter is entering.
        var previousPayload = await db.IoTStatusLogs
            .AsNoTracking()
            .Where(log => log.IoTDeviceId == device.IoTDeviceId &&
                          log.Topic == topic &&
                          log.LoggedAt >= checkedInAt &&
                          log.LoggedAt < receivedAt)
            .OrderByDescending(log => log.LoggedAt)
            .Select(log => log.Message)
            .FirstOrDefaultAsync();

        if (!TryReadTelemetry(previousPayload, out var previousDistanceCm, out _) ||
            !previousDistanceCm.HasValue || previousDistanceCm.Value > 50)
        {
            return;
        }

        if (!_pendingAutoLocks.TryAdd(device.IoTDeviceId, booking.BookingId))
            return;

        IoTDeviceLogWriter.Add(db, device, "vehicle_departure_detected", "confirmed", receivedAt,
            booking.BookingId, $"HC-SR04 changed from <=50 cm to {distanceCm.Value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)} cm while bollard was unlocked.");

        try
        {
            await PublishCommandAsync(device.Esp32Serial, $"AUTO_LOCK|{booking.BookingId}");
            IoTDeviceLogWriter.Add(db, device, "mqtt_auto_lock_command", "published", DateTime.UtcNow,
                booking.BookingId, "Automatic lock command published after vehicle departure detection.");
            _logger.LogInformation(
                "Vehicle departure detected for booking {BookingId}; AUTO_LOCK published to device {DeviceId}",
                booking.BookingId, device.Esp32Serial);
        }
        catch (Exception ex)
        {
            _pendingAutoLocks.TryRemove(device.IoTDeviceId, out _);
            IoTDeviceLogWriter.Add(db, device, "mqtt_auto_lock_command", "failed", DateTime.UtcNow, booking.BookingId, ex.Message);
            _logger.LogError(ex, "Failed to publish AUTO_LOCK for booking {BookingId} on device {DeviceId}",
                booking.BookingId, device.Esp32Serial);
        }
    }

    private static bool TryReadTelemetry(string? payload, out double? distanceCm, out string? bollardPosition)
    {
        distanceCm = null;
        bollardPosition = null;
        if (string.IsNullOrWhiteSpace(payload)) return false;

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.TryGetProperty("distanceCm", out var distanceElement) &&
                distanceElement.ValueKind == JsonValueKind.Number &&
                distanceElement.TryGetDouble(out var parsedDistance) &&
                double.IsFinite(parsedDistance))
            {
                distanceCm = parsedDistance;
            }

            if (root.TryGetProperty("bollardPosition", out var positionElement) &&
                positionElement.ValueKind == JsonValueKind.String)
            {
                bollardPosition = positionElement.GetString();
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (_client.IsConnected)
        {
            await _client.DisconnectAsync();
        }
        _client.Dispose();
    }
}
