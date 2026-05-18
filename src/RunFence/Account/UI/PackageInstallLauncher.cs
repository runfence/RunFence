using RunFence.Core;
using RunFence.Launch;
using LaunchProcessInfo = RunFence.Launch.Tokens.ProcessInfo;

namespace RunFence.Account.UI;

public class PackageInstallLauncher(ILaunchFacade launchFacade) : IPackageInstallLauncher
{
    public PackageInstallLaunchResult Launch(string scriptPath, AccountLaunchIdentity identity)
    {
        var launchCommand = BuildInstallLaunchCommand(scriptPath);
        var powershellTarget = new ProcessLaunchTarget(
            "powershell.exe",
            Arguments: CommandLineHelper.JoinArgs(["-ExecutionPolicy", "Bypass", "-Command", launchCommand]));

        using var launch = launchFacade.LaunchFile(powershellTarget, identity, permissionPrompt: (_, _) => true);
        LaunchProcessInfo process = launch.DetachProcess()
                      ?? throw new InvalidOperationException("Package install script did not return a process handle.");
        return new PackageInstallLaunchResult(new InstallProcess(process), launch.MaintenanceWarnings);
    }

    private static string BuildInstallLaunchCommand(string scriptPath)
    {
        var scriptLiteral = ToPowerShellSingleQuotedLiteral(scriptPath);
        var closePrompt = ToPowerShellSingleQuotedLiteral(
            "Install failed. Review the errors above, then press Enter to close this window.");
        return "& {\n" +
               "$ErrorActionPreference = 'Stop'\n" +
               "try {\n" +
               $"    & {scriptLiteral}\n" +
               "    exit 0\n" +
               "} catch {\n" +
               "    Write-Error $_\n" +
               $"    Read-Host -Prompt {closePrompt} | Out-Null\n" +
               "    exit 1\n" +
               "}\n" +
               "}";
    }

    private static string ToPowerShellSingleQuotedLiteral(string value)
        => $"'{value.Replace("'", "''")}'";
}
