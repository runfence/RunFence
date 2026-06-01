using System.Diagnostics;

namespace RunFence.PrefTrans;

internal sealed class PrefTransTimeoutClockFactory : IPrefTransTimeoutClockFactory
{
    public IPrefTransTimeoutClock StartNew() => new PrefTransStopwatchTimeoutClock(Stopwatch.StartNew());
}
