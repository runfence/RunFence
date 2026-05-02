using RunFence.Infrastructure;

namespace RunFence.Launch.Tokens;

public sealed class JobKeeperLaunchProcessApi : IJobKeeperLaunchProcessApi
{
    public void AllowAnyForegroundWindow() =>
        ProcessLaunchNative.AllowSetForegroundWindow(ProcessLaunchNative.ASFW_ANY);

    public IntPtr OpenLaunchedProcess(int pid) =>
        ProcessLaunchNative.OpenProcess(
            ProcessLaunchNative.SYNCHRONIZE | ProcessNative.ProcessQueryLimitedInformation,
            false,
            (uint)pid);
}
