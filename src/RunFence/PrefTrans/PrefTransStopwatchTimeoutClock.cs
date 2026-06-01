using System.Diagnostics;

namespace RunFence.PrefTrans;

internal sealed class PrefTransStopwatchTimeoutClock(Stopwatch stopwatch) : IPrefTransTimeoutClock
{
    public long ElapsedMilliseconds => stopwatch.ElapsedMilliseconds;
}
