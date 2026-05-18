using RunFence.Launch.Tokens;

namespace RunFence.Launch;

public sealed class WindowsAppsRepairProcess(ProcessInfo processInfo) : IWindowsAppsRepairProcess
{
    public bool WaitForExit(int timeoutMs) => processInfo.WaitForExit(timeoutMs);

    public int ExitCode => processInfo.ExitCode;

    public void Kill() => processInfo.Kill();

    public void Dispose() => processInfo.Dispose();
}
