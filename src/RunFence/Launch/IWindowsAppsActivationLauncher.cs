using RunFence.Launch.Tokens;

namespace RunFence.Launch;

public interface IWindowsAppsActivationLauncher
{
    ProcessInfo? TryLaunch(
        ProcessLaunchTarget target,
        string packageIdentitySourcePath,
        AccountLaunchIdentity originalIdentity,
        AccountLaunchIdentity resolvedIdentity);
}
