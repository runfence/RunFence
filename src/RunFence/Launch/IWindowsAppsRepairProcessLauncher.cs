namespace RunFence.Launch;

public interface IWindowsAppsRepairProcessLauncher
{
    IWindowsAppsRepairProcess LaunchRepair(
        ProcessLaunchTarget repairTarget,
        AccountLaunchIdentity originalIdentity,
        AccountLaunchIdentity resolvedIdentity);
}
