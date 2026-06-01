using RunFence.Account;
using RunFence.Launch.Tokens;

namespace RunFence.Launch;

public sealed class WindowsAppsActivationHelperLauncher(
    IAccountProcessLauncher accountProcessLauncher,
    IProfileRepairHelper profileRepairHelper) : IWindowsAppsActivationHelperLauncher
{
    public IWindowsAppsActivationHelperProcess? Launch(
        WindowsAppsActivationTarget activationTarget,
        AccountLaunchIdentity originalIdentity,
        AccountLaunchIdentity resolvedIdentity)
    {
        var processInfo = profileRepairHelper.ExecuteWithProfileRepair(
            () => accountProcessLauncher.Launch(activationTarget.HelperTarget, resolvedIdentity),
            originalIdentity.Sid);
        return processInfo == null ? null : new WindowsAppsActivationHelperProcessAdapter(processInfo);
    }
}
