namespace RunFence.JobKeeper;

public sealed class JobKeeperLifetimeController(
    IJobKeeperClock clock,
    IJobKeeperChildProcessRegistry childProcessRegistry)
    : IJobKeeperLifetimeController
{
    internal static readonly long IdleTimeoutMilliseconds = (long)TimeSpan.FromSeconds(15).TotalMilliseconds;
    internal static readonly long ChildSampleIntervalMilliseconds = (long)TimeSpan.FromSeconds(5).TotalMilliseconds;

    private long _lastRequestAt = clock.GetMilliseconds();
    private long _lastChildSampleAt = clock.GetMilliseconds();
    private int _consecutiveZeroChildSamples;

    public void RecordRequestArrival()
    {
        var now = clock.GetMilliseconds();
        Interlocked.Exchange(ref _lastRequestAt, now);
        Interlocked.Exchange(ref _lastChildSampleAt, now);
        Interlocked.Exchange(ref _consecutiveZeroChildSamples, 0);
    }

    public bool ShouldExit()
    {
        var now = clock.GetMilliseconds();
        SampleChildProcessesIfDue(now);

        var idleFor = now - Interlocked.Read(ref _lastRequestAt);
        if (idleFor < IdleTimeoutMilliseconds)
            return false;

        if (Volatile.Read(ref _consecutiveZeroChildSamples) * ChildSampleIntervalMilliseconds < IdleTimeoutMilliseconds)
            return false;

        if (childProcessRegistry.TryExitAfterCleaningIgnoredProcesses())
            return true;

        Interlocked.Exchange(ref _consecutiveZeroChildSamples, 0);
        return false;
    }

    private void SampleChildProcessesIfDue(long now)
    {
        var lastSampleAt = Interlocked.Read(ref _lastChildSampleAt);
        if (now - lastSampleAt < ChildSampleIntervalMilliseconds)
            return;

        var activeChildCount = childProcessRegistry.PruneExitedAndCountActive();
        Interlocked.Exchange(ref _lastChildSampleAt, now);
        if (activeChildCount == 0)
        {
            Interlocked.Increment(ref _consecutiveZeroChildSamples);
            return;
        }

        Interlocked.Exchange(ref _consecutiveZeroChildSamples, 0);
    }
}
