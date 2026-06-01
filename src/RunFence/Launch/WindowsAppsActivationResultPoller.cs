namespace RunFence.Launch;

public sealed class WindowsAppsActivationResultPoller : IWindowsAppsActivationResultPoller
{
    public DateTime UtcNow => DateTime.UtcNow;

    public void Sleep(TimeSpan interval)
    {
        Thread.Sleep(interval);
    }
}
