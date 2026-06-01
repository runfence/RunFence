namespace RunFence.AppxLauncher;

public sealed class SystemAppxLaunchVerificationClock : IAppxLaunchVerificationClock
{
    public DateTime UtcNow => DateTime.UtcNow;

    public void Sleep(TimeSpan duration)
    {
        if (duration > TimeSpan.Zero)
            Thread.Sleep(duration);
    }
}
