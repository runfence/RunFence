using RunFence.Infrastructure;

namespace RunFence.RunAs;

/// <summary>
/// DOS protection for RunAs requests. Blocks requests if too many declines occur
/// within a time window (4 declines in 4 min) or if a decline happened recently (&lt;15s).
/// </summary>
public class RunAsDosProtection(IStopwatchProvider stopwatch)
{
    private int _declineCount;
    private long _declineWindowStart;
    private long _declineLastTime;
    private readonly object _lock = new();

    public bool IsBlocked()
    {
        lock (_lock)
        {
            var now = stopwatch.GetTimestamp();

            switch (_declineCount)
            {
                case > 0 when stopwatch.GetElapsedSeconds(_declineLastTime, now) < 15:
                case >= 4 when stopwatch.GetElapsedSeconds(_declineWindowStart, now) < 240:
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
            var now = stopwatch.GetTimestamp();

            if (_declineCount == 0 || stopwatch.GetElapsedSeconds(_declineWindowStart, now) >= 240)
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