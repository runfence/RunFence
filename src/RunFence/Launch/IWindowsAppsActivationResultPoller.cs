namespace RunFence.Launch;

public interface IWindowsAppsActivationResultPoller
{
    DateTime UtcNow { get; }
    void Sleep(TimeSpan interval);
}
