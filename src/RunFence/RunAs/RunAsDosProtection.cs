using RunFence.Infrastructure;

namespace RunFence.RunAs;

/// <summary>
/// DOS protection for RunAs requests. Blocks requests if too many declines occur
/// within a time window (4 declines in 4 min) or if a decline happened recently (&lt;15s).
/// </summary>
public class RunAsDosProtection
{
    private readonly IStopwatchProvider _stopwatch;

    private int _declineCount;
    private long _declineWindowStart;
    private long _declineLastTime;
    private readonly object _lock = new();

    public RunAsDosProtection(IStopwatchProvider stopwatch)
    {
        _stopwatch = stopwatch;
    }

    public bool IsBlocked()
    {
        lock (_lock)
        {
            var now = _stopwatch.GetTimestamp();

            switch (_declineCount)
            {
                case > 0 when _stopwatch.GetElapsedSeconds(_declineLastTime, now) < 15:
                case >= 4 when _stopwatch.GetElapsedSeconds(_declineWindowStart, now) < 240:
                    return true;
                default:
                    return false;
            }
        }
    }

    public void RecordDecline()
    {
        lock (_lock)
        {
            var now = _stopwatch.GetTimestamp();

            if (_declineCount == 0 || _stopwatch.GetElapsedSeconds(_declineWindowStart, now) >= 240)
            {
                _declineCount = 1;
                _declineWindowStart = now;
            }
            else
            {
                _declineCount++;
            }

            _declineLastTime = now;
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _declineCount = 0;
            _declineLastTime = 0;
            _declineWindowStart = 0;
        }
    }
}