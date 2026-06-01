namespace RunFence.AppxLauncher;

public interface IAppxLaunchVerificationClock
{
    DateTime UtcNow { get; }

    void Sleep(TimeSpan duration);
}
