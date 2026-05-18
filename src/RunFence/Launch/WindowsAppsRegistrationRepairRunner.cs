using RunFence.Core;

namespace RunFence.Launch;

public sealed class WindowsAppsRegistrationRepairRunner(
    IWindowsAppsPackageRegistrationRepairer windowsAppsPackageRegistrationRepairer,
    IWindowsAppsRepairProcessLauncher repairProcessLauncher,
    ILoggingService log) : IWindowsAppsRegistrationRepairRunner
{
    private const int RegistrationTimeoutMs = 30_000;

    public bool TryRepair(
        ProcessLaunchTarget failedTarget,
        AccountLaunchIdentity originalIdentity,
        AccountLaunchIdentity resolvedIdentity)
    {
        var repairTarget = windowsAppsPackageRegistrationRepairer.TryCreateRepairTarget(failedTarget);
        if (repairTarget == null)
            return false;

        try
        {
            using var repairProcess = repairProcessLauncher.LaunchRepair(repairTarget, originalIdentity, resolvedIdentity);

            if (!repairProcess.WaitForExit(RegistrationTimeoutMs))
            {
                repairProcess.Kill();
                log.Warn($"WindowsApps package registration repair timed out for '{failedTarget.ExePath}'.");
                return false;
            }

            if (repairProcess.ExitCode == 0)
                return true;

            log.Warn(
                $"WindowsApps package registration repair failed for '{failedTarget.ExePath}' with exit code {repairProcess.ExitCode}.");
            return false;
        }
        catch (Exception repairEx)
        {
            log.Warn($"WindowsApps package registration repair failed for '{failedTarget.ExePath}': {repairEx.Message}");
            return false;
        }
    }
}
