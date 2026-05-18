namespace RunFence.JobKeeper;

public sealed class EnvironmentTickCountClock : IJobKeeperClock
{
    public long GetMilliseconds() => Environment.TickCount64;
}
