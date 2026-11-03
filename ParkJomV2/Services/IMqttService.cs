namespace ParkJomV2.Services;

public interface IMqttService
{
    Task PublishCommandAsync(string deviceUuid, string command, CancellationToken cancellationToken = default);
}
