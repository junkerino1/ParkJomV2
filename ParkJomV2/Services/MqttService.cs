using System.Text;
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
        if (!_client.IsConnected)
        {
            throw new InvalidOperationException("The MQTT broker is not connected.");
        }

        var message = new MqttApplicationMessageBuilder()
            .WithTopic($"{_topicRoot}/{deviceUuid}/commands")
            .WithPayload(command)
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

        try
        {
            await _client.PublishAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
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
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process an inbound MQTT message");
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
