using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using ParkJomV2.Data;
using ParkJomV2.DTOs;
using ParkJomV2.Models;
using ParkJomV2.Models.Enums;
using ParkJomV2.Services.Notifications;

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
    private readonly bool _useTls;
    private readonly string? _caCertificatePath;
    private readonly TimeSpan _commandAckTimeout;
    private readonly ConcurrentDictionary<int, int> _pendingAutoLocks = new();
    private readonly ConcurrentDictionary<PendingCommandKey, PendingCommand> _pendingCommands = new();

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
        _useTls = bool.TryParse(configuration["Mqtt:Broker:UseTls"], out var useTls) && useTls;
        _caCertificatePath = configuration["Mqtt:Broker:CaCertificatePath"];
        var ackTimeoutSeconds = int.TryParse(configuration["IoT:CommandAckTimeoutSeconds"], out var configuredTimeout)
            ? Math.Clamp(configuredTimeout, 2, 60)
            : 15;
        _commandAckTimeout = TimeSpan.FromSeconds(ackTimeoutSeconds);

        if (string.IsNullOrWhiteSpace(_username) != string.IsNullOrWhiteSpace(_password))
        {
            throw new InvalidOperationException("Configure both Mqtt:Broker:Username and Mqtt:Broker:Password, or leave both empty.");
        }

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

        if (_useTls)
        {
            var tlsOptionsBuilder = new MqttClientTlsOptionsBuilder()
                .UseTls()
                .WithTargetHost(_host);

            if (!string.IsNullOrWhiteSpace(_caCertificatePath))
            {
                if (!File.Exists(_caCertificatePath))
                {
                    throw new InvalidOperationException(
                        $"The configured MQTT CA certificate file was not found at '{_caCertificatePath}'.");
                }

                var trustChain = new X509Certificate2Collection();
                trustChain.ImportFromPem(File.ReadAllText(_caCertificatePath));
                if (trustChain.Count == 0)
                {
                    throw new InvalidOperationException("The configured MQTT CA certificate file contains no PEM certificates.");
                }

                tlsOptionsBuilder.WithTrustChain(trustChain);
            }

            optionsBuilder.WithTlsOptions(tlsOptionsBuilder.Build());
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
                    _logger.LogInformation("Connected to MQTT broker {Host}:{Port} (TLS: {TlsEnabled}) and subscribed to device ACK and telemetry topics", _host, _port, _useTls);
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

    public async Task<MqttCommandResult> PublishCommandAndWaitForAckAsync(
        string deviceUuid,
        int bookingId,
        string command,
        CancellationToken cancellationToken = default)
    {
        var normalizedCommand = command.Trim().ToUpperInvariant();
        var key = new PendingCommandKey(deviceUuid.Trim().ToUpperInvariant(), bookingId, normalizedCommand);
        var completion = new TaskCompletionSource<MqttCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var commandId = Guid.NewGuid().ToString("N").ToUpperInvariant();
        if (!_pendingCommands.TryAdd(key, new PendingCommand(commandId, completion)))
        {
            return new MqttCommandResult(MqttCommandWaitStatus.Busy, deviceUuid, bookingId, normalizedCommand,
                Message: "A matching device command is already waiting for acknowledgement.");
        }

        try
        {
            await PublishCommandAsync(deviceUuid, $"{normalizedCommand}|{bookingId}|{commandId}", cancellationToken);
            try
            {
                return await completion.Task.WaitAsync(_commandAckTimeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                return new MqttCommandResult(MqttCommandWaitStatus.TimedOut, deviceUuid, bookingId, normalizedCommand, commandId,
                    Message: $"No device acknowledgement arrived within {_commandAckTimeout.TotalSeconds:0} seconds.");
            }
        }
        finally
        {
            _pendingCommands.TryRemove(key, out _);
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
            var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
            var walletService = scope.ServiceProvider.GetRequiredService<WalletService>();
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
                await ProcessDeviceAcknowledgementAsync(db, device, payload, receivedAt, notifications, walletService);
            }
            else
            {
                await ProcessTelemetryAsync(db, device, topic, payload, receivedAt);
            }

            await db.SaveChangesAsync();
            if (suffix[1] == "ack")
            {
                CompletePendingCommand(suffix[0], payload);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process an inbound MQTT message");
        }
    }

    private void CompletePendingCommand(string deviceUuid, string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (!root.TryGetProperty("command", out var commandElement) || commandElement.ValueKind != JsonValueKind.String ||
                !root.TryGetProperty("bookingId", out var bookingElement) || !bookingElement.TryGetInt32(out var bookingId))
            {
                return;
            }

            var command = commandElement.GetString()?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(command)) return;
            var commandId = ReadJsonString(root, "commandId")?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(commandId)) return;

            var result = ReadJsonString(root, "result") ?? ReadJsonString(root, "status");
            var message = ReadJsonString(root, "message") ?? ReadJsonString(root, "reason") ??
                          ReadJsonString(root, "error") ?? ReadJsonString(root, "details");
            var errorCode = ReadJsonString(root, "errorCode") ?? ReadJsonString(root, "code");
            var bollardPosition = ReadJsonString(root, "bollardPosition");
            double? distanceCm = root.TryGetProperty("distanceCm", out var distanceElement) &&
                                 distanceElement.ValueKind == JsonValueKind.Number && distanceElement.TryGetDouble(out var parsedDistance) &&
                                 double.IsFinite(parsedDistance)
                ? parsedDistance
                : null;
            var key = new PendingCommandKey(deviceUuid.Trim().ToUpperInvariant(), bookingId, command);
            if (_pendingCommands.TryGetValue(key, out var pending) &&
                string.Equals(pending.CommandId, commandId, StringComparison.Ordinal))
            {
                pending.Completion.TrySetResult(new MqttCommandResult(
                    MqttCommandWaitStatus.Acknowledged,
                    deviceUuid,
                    bookingId,
                    command,
                    commandId,
                    result,
                    message,
                    errorCode,
                    distanceCm,
                    payload,
                    bollardPosition));
            }
        }
        catch (JsonException)
        {
            // The existing ACK processor records malformed payloads; malformed ACKs cannot complete a request.
        }
    }

    private static string? ReadJsonString(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    private readonly record struct PendingCommandKey(string DeviceUuid, int BookingId, string Command);
    private sealed record PendingCommand(string CommandId, TaskCompletionSource<MqttCommandResult> Completion);

    private async Task ProcessDeviceAcknowledgementAsync(
        ApplicationDbContext db,
        IoTDevice device,
        string payload,
        DateTime receivedAt,
        INotificationService notifications,
        WalletService walletService)
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
            bookingId > 0 ? bookingId : null, $"command={command ?? "unknown"}");

        if (string.Equals(command, "AUTO_LOCK", StringComparison.OrdinalIgnoreCase))
            _pendingAutoLocks.TryRemove(device.IoTDeviceId, out _);

        if (bookingId < 0)
        {
            // Owner/admin commands by spot or device are deliberately unassociated with
            // renter bookings; acknowledge and log them without recording entry or exit.
            if (string.Equals(command, "UNLOCK", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(command, "LOCK", StringComparison.OrdinalIgnoreCase))
            {
                IoTDeviceLogWriter.Add(db, device, "unassociated_device_command_ack", result ?? "missing_result",
                    receivedAt, details: $"command={command}; payload={payload}");
            }
            return;
        }

        if (!string.Equals(result, "SUCCESS", StringComparison.OrdinalIgnoreCase))
            return;

        var isUnlock = string.Equals(command, "UNLOCK", StringComparison.OrdinalIgnoreCase);
        var isLock = string.Equals(command, "LOCK", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(command, "AUTO_LOCK", StringComparison.OrdinalIgnoreCase);
        if (!device.ParkingSpotId.HasValue || (!isUnlock && !isLock))
        {
            return;
        }

        var booking = await db.Bookings
            .Include(b => b.Vehicle)
            .Include(b => b.ParkingSpot)
                .ThenInclude(spot => spot.ParkingAvailabilities)
            .FirstOrDefaultAsync(b =>
                b.BookingId == bookingId && b.ParkingSpotId == device.ParkingSpotId.Value);
        if (booking is null)
        {
            _logger.LogWarning("IoT ACK references booking {BookingId} that is not assigned to device {DeviceId}", bookingId, device.Esp32Serial);
            IoTDeviceLogWriter.Add(db, device, "device_ack_booking_validation", "rejected", receivedAt,
                bookingId, "Booking does not belong to the device's assigned parking spot.");
            return;
        }

        var vehiclePlate = booking.Vehicle.NumberPlate;
        var spotId = booking.ParkingSpotId;
        if (isUnlock && booking.BookingStatus is BookingStatus.Confirmed or BookingStatus.Active)
        {
            // Treat a successful physical unlock ACK as an entry event. Preserve the booking's
            // first entry timestamp across later daily entries.
            var isFirstEntry = !booking.CheckedInAt.HasValue;
            booking.CheckedInAt ??= receivedAt;
            booking.BookingStatus = BookingStatus.Active;
            booking.UpdatedAt = receivedAt;
            IoTDeviceLogWriter.Add(db, device, "unlock_ack_booking_checkin", "completed", receivedAt,
                bookingId, isFirstEntry ? "First entry confirmed by successful ESP32 ACK." : "Additional entry confirmed by successful ESP32 ACK.");
            IoTDeviceLogWriter.Add(db, device, "parking_entry_confirmed", "success", receivedAt, bookingId,
                $"vehiclePlate={vehiclePlate}; parkingSpotId={spotId}; firstEntry={isFirstEntry}; eventAtUtc={receivedAt:O}");
        }
        else if (isLock &&
                 booking.BookingStatus == BookingStatus.Active &&
                 booking.CheckedInAt.HasValue &&
                 !booking.ActualExitAt.HasValue)
        {
            // The ESP32 only reports LOCK success after its live obstacle check passes.
            // Record every daily exit; only the final scheduled booking date sets ActualExitAt.
            await ApplyDailyOverstayPenaltiesAsync(db, device, booking, receivedAt, notifications);
            var isFinalBookingExit = IsOnOrAfterLastAvailableDate(booking, receivedAt);
            if (isFinalBookingExit)
            {
                booking.ActualExitAt = receivedAt;
                booking.BookingStatus = BookingStatus.Completed;
                await SettleCompletedBookingPayoutAsync(db, device, booking, receivedAt, walletService);
            }
            booking.UpdatedAt = receivedAt;
            IoTDeviceLogWriter.Add(db, device, "parking_exit_confirmed", "success", receivedAt, bookingId,
                $"vehiclePlate={vehiclePlate}; parkingSpotId={spotId}; finalBookingExit={isFinalBookingExit}; eventAtUtc={receivedAt:O}");
            if (isFinalBookingExit)
            {
                IoTDeviceLogWriter.Add(db, device, "booking_completed_after_final_exit", "completed", receivedAt,
                    bookingId, "Final scheduled parking date exit confirmed by successful ESP32 ACK.");
            }
        }
    }

    private static bool IsOnOrAfterLastAvailableDate(Booking booking, DateTime eventAtUtc)
    {
        var startDate = DateOnly.FromDateTime(booking.StartDate);
        var endDateExclusive = DateOnly.FromDateTime(booking.EndDate);
        DateOnly? lastAvailableDate = null;
        for (var date = startDate; date < endDateExclusive; date = date.AddDays(1))
        {
            if (HasAvailabilityRuleForDate(booking.ParkingSpot.ParkingAvailabilities, date))
                lastAvailableDate = date;
        }

        if (!lastAvailableDate.HasValue)
            return DateOnly.FromDateTime(eventAtUtc.AddHours(8)) >= endDateExclusive;

        return DateOnly.FromDateTime(eventAtUtc.AddHours(8)) >= lastAvailableDate.Value;
    }

    private static bool HasAvailabilityRuleForDate(IEnumerable<Availability> rules, DateOnly date)
        => rules.Any(rule =>
            (!rule.EffectiveFrom.HasValue || date >= rule.EffectiveFrom.Value) &&
            (!rule.EffectiveUntil.HasValue || date <= rule.EffectiveUntil.Value) &&
            (rule.DayType == DayType.Everyday ||
             (rule.DayType == DayType.Weekday && date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) ||
             (rule.DayType == DayType.Weekend && date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)));

    private async Task ApplyDailyOverstayPenaltiesAsync(
        ApplicationDbContext db,
        IoTDevice device,
        Booking booking,
        DateTime exitAtUtc,
        INotificationService notifications)
    {
        var firstDate = DateOnly.FromDateTime((booking.CheckedInAt ?? exitAtUtc).AddHours(8));
        var lastDate = DateOnly.FromDateTime(exitAtUtc.AddHours(8));
        for (var date = firstDate; date <= lastDate; date = date.AddDays(1))
        {
            var rules = GetAvailabilityRulesForDate(booking.ParkingSpot.ParkingAvailabilities, date).ToList();
            if (rules.Count == 0 || IsAllDayRental(rules)) continue;

            var dailyClose = rules.Max(rule => rule.EndTime);
            var graceEndsAtUtc = new DateTimeOffset(date.ToDateTime(dailyClose), TimeSpan.FromHours(8))
                .AddMinutes(15).UtcDateTime;
            var lateMinutes = (int)Math.Ceiling((exitAtUtc - graceEndsAtUtc).TotalMinutes);
            if (lateMinutes <= 0) continue;

            var billableHours = (lateMinutes + 59) / 60;
            var penalty = Math.Min(billableHours * 5m, 30m);
            var reference = $"OV-{booking.BookingId}-{date:yyyyMMdd}";
            var alreadyRecorded = await db.Transactions.AnyAsync(t =>
                t.BookingId == booking.BookingId && t.TransactionType == TransactionType.OverstayPenalty &&
                t.ReferenceNumber == reference);
            if (alreadyRecorded) continue;

            var renterWallet = await db.Wallets.FirstOrDefaultAsync(w =>
                w.UserId == booking.RenterId && w.Status == WalletStatus.Active);
            var ownerWallet = await db.Wallets.FirstOrDefaultAsync(w =>
                w.UserId == booking.ParkingSpot.OwnerId && w.Status == WalletStatus.Active);
            var paid = renterWallet is not null && ownerWallet is not null && renterWallet.Balance >= penalty;
            var now = exitAtUtc;

            if (paid)
            {
                renterWallet!.Balance -= penalty;
                renterWallet.UpdatedAt = now;
                ownerWallet!.Balance += penalty;
                ownerWallet.UpdatedAt = now;
                db.Transactions.Add(new Transaction
                {
                    WalletId = renterWallet.WalletId, BookingId = booking.BookingId,
                    TransactionType = TransactionType.OverstayPenalty, Amount = -penalty,
                    PaymentMethod = PaymentMethod.Wallet, TransactionStatus = TransactionStatus.Completed,
                    ReferenceNumber = reference, CreatedAt = now, UpdatedAt = now
                });
                db.Transactions.Add(new Transaction
                {
                    WalletId = ownerWallet.WalletId, BookingId = booking.BookingId,
                    TransactionType = TransactionType.OverstayPenalty, Amount = penalty,
                    PaymentMethod = PaymentMethod.Wallet, TransactionStatus = TransactionStatus.Completed,
                    ReferenceNumber = reference, CreatedAt = now, UpdatedAt = now
                });
            }
            else
            {
                db.Transactions.Add(new Transaction
                {
                    WalletId = renterWallet?.WalletId, BookingId = booking.BookingId,
                    TransactionType = TransactionType.OverstayPenalty, Amount = -penalty,
                    PaymentMethod = PaymentMethod.Wallet, TransactionStatus = TransactionStatus.Pending,
                    ReferenceNumber = reference, CreatedAt = now, UpdatedAt = now
                });
            }

            booking.OverstayHours += billableHours;
            booking.OverstayPenaltyAmount += penalty;
            booking.UpdatedAt = now;
            IoTDeviceLogWriter.Add(db, device, "overstay_penalty_assessed", paid ? "paid" : "payment_due", now,
                booking.BookingId,
                $"windowDate={date:yyyy-MM-dd}; hours={billableHours}; amount=RM{penalty:F2}; reference={reference}");

            try
            {
                await notifications.PublishAsync(new NotificationPublishRequest(
                    booking.RenterId,
                    paid ? "booking.overstay_charged" : "booking.overstay_payment_due",
                    new Dictionary<string, object?>
                    {
                        ["bookingId"] = booking.BookingId,
                        ["penaltyAmount"] = $"RM {penalty:F2}",
                        ["overstayHours"] = billableHours,
                        ["windowDate"] = date.ToString("yyyy-MM-dd")
                    },
                    entityType: "Booking", entityId: booking.BookingId.ToString(),
                    dedupeKey: $"booking.overstay_penalty:{booking.BookingId}:{date:yyyyMMdd}"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not notify renter about overstay penalty for booking {BookingId}", booking.BookingId);
            }
        }
    }

    private async Task SettleCompletedBookingPayoutAsync(
        ApplicationDbContext db,
        IoTDevice device,
        Booking booking,
        DateTime completedAtUtc,
        WalletService walletService)
    {
        if (booking.OwnerPayoutAmount <= 0) return;
        var ownerWallet = await db.Wallets.FirstOrDefaultAsync(w =>
            w.UserId == booking.ParkingSpot.OwnerId && w.Status == WalletStatus.Active);
        if (ownerWallet is null || ownerWallet.OnHold < booking.OwnerPayoutAmount)
        {
            IoTDeviceLogWriter.Add(db, device, "owner_payout_settlement", "failed", completedAtUtc,
                booking.BookingId, "Owner wallet is missing or held balance is below the booking payout.");
            return;
        }

        walletService.SettleOwnerPayout(ownerWallet, booking.OwnerPayoutAmount, completedAtUtc);
        IoTDeviceLogWriter.Add(db, device, "owner_payout_settlement", "completed", completedAtUtc,
            booking.BookingId, $"amount=RM{booking.OwnerPayoutAmount:F2}");
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
            !distanceCm.HasValue)
        {
            return;
        }

        var booking = await db.Bookings
            .Include(b => b.Vehicle)
            .Include(b => b.ParkingSpot)
                .ThenInclude(spot => spot.ParkingAvailabilities)
            .Where(b => b.ParkingSpotId == device.ParkingSpotId.Value &&
                        b.BookingStatus == BookingStatus.Active &&
                        b.CheckedInAt != null &&
                        b.ActualExitAt == null)
            .OrderByDescending(b => b.CheckedInAt)
            .FirstOrDefaultAsync();
        if (booking?.CheckedInAt is not DateTime checkedInAt)
            return;

        if (distanceCm.Value <= 50) return;

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

    private static IEnumerable<Availability> GetAvailabilityRulesForDate(IEnumerable<Availability> rules, DateOnly date)
        => rules.Where(rule =>
            (!rule.EffectiveFrom.HasValue || date >= rule.EffectiveFrom.Value) &&
            (!rule.EffectiveUntil.HasValue || date <= rule.EffectiveUntil.Value) &&
            (rule.DayType == DayType.Everyday ||
             (rule.DayType == DayType.Weekday && date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) ||
             (rule.DayType == DayType.Weekend && date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)));

    private static bool IsAllDayRental(IEnumerable<Availability> rules)
        => rules.Any(rule => rule.DayType == DayType.Everyday &&
                             rule.StartTime == TimeOnly.MinValue &&
                             rule.EndTime == new TimeOnly(23, 59));

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
