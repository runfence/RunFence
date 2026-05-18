namespace RunFence.ProfileKeeper;

public sealed record ProfileKeeperOptions(TimeSpan ScanInterval, TimeSpan IdleGracePeriod)
{
    public static ProfileKeeperOptions Default { get; } = new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
}
