using RunFence.Core;
using RunFence.Launching.Resolution;

namespace RunFence.Launch;

public sealed class WindowsAppsPackageRegistrationRepairer(
    IWindowsAppsPackageIdentityResolver packageIdentityResolver) : IWindowsAppsPackageRegistrationRepairer
{
    public ProcessLaunchTarget? TryCreateRepairTarget(ProcessLaunchTarget failedTarget)
    {
        if (!packageIdentityResolver.TryResolvePackageFamilyName(failedTarget.ExePath, out var packageFamilyName))
            return null;

        var command = "Add-AppxPackage -RegisterByFamilyName -MainPackage "
                      + ToPowerShellSingleQuotedLiteral(packageFamilyName)
                      + " -ErrorAction Stop";
        return new ProcessLaunchTarget(
            "powershell.exe",
            CommandLineHelper.MaterializeProcessArguments(
                [
                    "-NoProfile",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-Command",
                    command
                ]),
            HideWindow: true,
            SuppressStartupFeedback: true);
    }

    private static string ToPowerShellSingleQuotedLiteral(string value)
        => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}
