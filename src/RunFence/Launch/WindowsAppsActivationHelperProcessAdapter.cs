using RunFence.Launch.Tokens;

namespace RunFence.Launch;

public sealed class WindowsAppsActivationHelperProcessAdapter(ProcessInfo processInfo) : IWindowsAppsActivationHelperProcess
{
    public bool HasExited => processInfo.HasExited;

    public int ExitCode => processInfo.ExitCode;

    public bool WaitForExit(int timeoutMs) => processInfo.WaitForExit(timeoutMs);

    public void Dispose()
    {
        processInfo.Dispose();
    }
}
