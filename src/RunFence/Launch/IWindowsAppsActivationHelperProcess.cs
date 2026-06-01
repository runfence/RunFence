namespace RunFence.Launch;

public interface IWindowsAppsActivationHelperProcess : IDisposable
{
    bool HasExited { get; }
    int ExitCode { get; }
    bool WaitForExit(int timeoutMs);
}
