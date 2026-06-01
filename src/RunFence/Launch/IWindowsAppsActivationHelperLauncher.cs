namespace RunFence.Launch;

public interface IWindowsAppsActivationHelperLauncher
{
    IWindowsAppsActivationHelperProcess? Launch(
        WindowsAppsActivationTarget activationTarget,
        AccountLaunchIdentity originalIdentity,
        AccountLaunchIdentity resolvedIdentity);
}
