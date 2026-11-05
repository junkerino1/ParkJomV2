namespace ParkJomV2.Services;

public interface IMqttService
{
    Task PublishCommandAsync(string deviceUuid, string command, CancellationToken cancellationToken = default);

    Task<MqttCommandResult> PublishCommandAndWaitForAckAsync(
        string deviceUuid,
        int bookingId,
        string command,
        CancellationToken cancellationToken = default);
}

public enum MqttCommandWaitStatus
{
    Acknowledged,
    TimedOut,
    Busy
}

public sealed record MqttCommandResult(
    MqttCommandWaitStatus WaitStatus,
    string DeviceUuid,
    int BookingId,
    string Command,
    string? CommandId = null,
    string? Result = null,
    string? Message = null,
    string? ErrorCode = null,
    double? DistanceCm = null,
    string? RawPayload = null,
    string? BollardPosition = null);
