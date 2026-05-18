namespace RunFence.Launch;

public interface IWindowsAppsRepairProcess : IDisposable
{
    bool WaitForExit(int timeoutMs);

    int ExitCode { get; }

    void Kill();
}
