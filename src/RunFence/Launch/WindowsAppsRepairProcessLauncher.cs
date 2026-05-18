using RunFence.Account;
using RunFence.Launch.Tokens;

namespace RunFence.Launch;

public sealed class WindowsAppsRepairProcessLauncher(
    IAccountProcessLauncher accountProcessLauncher,
    IProfileRepairHelper profileRepairHelper) : IWindowsAppsRepairProcessLauncher
{
    public IWindowsAppsRepairProcess LaunchRepair(
        ProcessLaunchTarget repairTarget,
        AccountLaunchIdentity originalIdentity,
        AccountLaunchIdentity resolvedIdentity)
    {
        return profileRepairHelper.ExecuteWithProfileRepair(
            () => new WindowsAppsRepairProcess(accountProcessLauncher.Launch(repairTarget, resolvedIdentity)),
            originalIdentity.Sid);
    }
}
