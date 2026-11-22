using ParkJomV2.DTOs;

namespace ParkJomV2.Services.Overstay;

public sealed class OverstayWorkerStatusTracker
{
    private readonly object _gate = new();
    private bool _enabled = true;
    private bool _isRunning = true;
    private int _intervalSeconds = 60;
    private DateTime? _startedAtUtc = DateTime.UtcNow;
    private DateTime? _lastScanStartedAtUtc;
    private DateTime? _lastScanCompletedAtUtc;
    private int _lastScanDetectedCount;
    private int _lastScanWarningsSent;
    private int _lastScanPenaltiesDebited;
    private long _totalScansCompleted;
    private string? _lastError;

    public void MarkStarted(bool enabled, int intervalSeconds)
    {
        lock (_gate)
        {
            _enabled = enabled;
            _isRunning = enabled;
            _intervalSeconds = intervalSeconds;
            _startedAtUtc = DateTime.UtcNow;
        }
    }

    public void MarkStopped()
    {
        lock (_gate)
        {
            _isRunning = false;
        }
    }

    public void RecordScanStarted()
    {
        lock (_gate)
        {
            _lastScanStartedAtUtc = DateTime.UtcNow;
        }
    }

    public void RecordScanSucceeded(int detected, int warningsSent, int penaltiesDebited)
    {
        lock (_gate)
        {
            _lastScanCompletedAtUtc = DateTime.UtcNow;
            _lastScanDetectedCount = detected;
            _lastScanWarningsSent = warningsSent;
            _lastScanPenaltiesDebited = penaltiesDebited;
            _totalScansCompleted++;
            _lastError = null;
        }
    }

    public void RecordScanFailed(string error)
    {
        lock (_gate)
        {
            _lastScanCompletedAtUtc = DateTime.UtcNow;
            _lastError = error;
        }
    }

    public OverstayWorkerStatusDTO Snapshot()
    {
        lock (_gate)
        {
            return new OverstayWorkerStatusDTO
            {
                Enabled = _enabled,
                IsRunning = _isRunning,
                IntervalSeconds = _intervalSeconds,
                StartedAtUtc = _startedAtUtc,
                LastScanStartedAtUtc = _lastScanStartedAtUtc,
                LastScanCompletedAtUtc = _lastScanCompletedAtUtc,
                LastScanDetectedCount = _lastScanDetectedCount,
                LastScanWarningsSent = _lastScanWarningsSent,
                LastScanPenaltiesDebited = _lastScanPenaltiesDebited,
                TotalScansCompleted = _totalScansCompleted,
                LastError = _lastError
            };
        }
    }
}
