namespace RunFence.Launch;

public interface IWindowsAppsActivationTargetFactory
{
    WindowsAppsActivationTarget? TryCreate(
        ProcessLaunchTarget failedTarget,
        string packageIdentitySourcePath,
        string targetSid);
}
