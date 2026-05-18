namespace RunFence.Launch;

public interface IWindowsAppsRegistrationRepairRunner
{
    bool TryRepair(
        ProcessLaunchTarget failedTarget,
        AccountLaunchIdentity originalIdentity,
        AccountLaunchIdentity resolvedIdentity);
}
