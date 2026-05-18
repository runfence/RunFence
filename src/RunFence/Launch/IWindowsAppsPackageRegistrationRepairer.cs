namespace RunFence.Launch;

public interface IWindowsAppsPackageRegistrationRepairer
{
    ProcessLaunchTarget? TryCreateRepairTarget(ProcessLaunchTarget failedTarget);
}
