using RunFence.Core;
using RunFence.Launch;

namespace RunFence.Account.UI;

/// <summary>
/// Handles launch operations (CMD, Folder Browser, Environment Variables, package install)
/// for account and container identities.
/// </summary>
public class ToolLauncher(
    ILaunchFacade launchFacade,
    AccountToolResolver toolResolver,
    PackageInstallService packageInstallService,
    ILoggingService log)
{
    public void OpenCmd(LaunchIdentity identity)
    {
        if (identity is AppContainerLaunchIdentity)
        {
            var cmdExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
            RunWithErrorHandling("Launch CMD",
                () => launchFacade.LaunchFile(new ProcessLaunchTarget(cmdExe), identity, permissionPrompt: (_, _) => true));
        }
        else
        {
            var terminalExe = toolResolver.ResolveTerminalExe(identity.Sid);
            var profilePath = toolResolver.GetProfileRoot(identity.Sid);
            RunWithErrorHandling("Launch CMD",
                () => launchFacade.LaunchFile(new ProcessLaunchTarget(terminalExe, WorkingDirectory: profilePath), identity, permissionPrompt: (_, _) => true));
        }
    }

    public void OpenFolderBrowser(LaunchIdentity identity, Func<string, string, bool>? permissionPrompt = null)
    {
        RunWithErrorHandling("Launch Folder Browser", () =>
            launchFacade.LaunchFolderBrowser(identity, folderPermissionPrompt: permissionPrompt));
    }

    public void OpenEnvironmentVariables(LaunchIdentity identity)
    {
        RunWithErrorHandling("Launch Environment Variables", () =>
            launchFacade.LaunchFile(new ProcessLaunchTarget("rundll32.exe", Arguments: "sysdm.cpl,EditEnvironmentVariables"), identity, permissionPrompt: (_, _) => true));
    }

    public void InstallPackage(InstallablePackage package, AccountLaunchIdentity identity)
    {
        RunWithErrorHandling($"Install {package.DisplayName}",
            () => packageInstallService.InstallPackages([package], identity));
    }

    public void InstallPackages(IReadOnlyList<InstallablePackage> packages, AccountLaunchIdentity identity)
    {
        RunWithErrorHandling("Install packages", () => packageInstallService.InstallPackages(packages, identity));
    }

    public bool IsWindowsTerminal(string sid) => toolResolver.ResolveTerminalExe(sid) != "cmd.exe";

    public bool IsPackageInstalled(InstallablePackage package, string sid) => packageInstallService.IsPackageInstalled(package, sid);

    private void RunWithErrorHandling(string label, Action launchAction)
    {
        try
        {
            launchAction();
        }
        catch (CredentialNotFoundException ex)
        {
            MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (MissingPasswordException ex)
        {
            MessageBox.Show(ex.Message, "Missing Password", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            log.Error($"{label} failed", ex);
            MessageBox.Show($"{label} failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
