using LaunchProcessInfo = RunFence.Launch.Tokens.ProcessInfo;

namespace RunFence.Account.UI;

public sealed class InstallProcess(LaunchProcessInfo processInfo) : IInstallProcess
{
    public bool HasExited => processInfo.HasExited;
    public int ExitCode => processInfo.ExitCode;

    public void Dispose()
    {
        processInfo.Dispose();
    }
}
