using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Acl;
using RunFence.Launch;

namespace RunFence.Account.UI;

/// <summary>
/// Handles launch operations (CMD, Folder Browser, Environment Variables, package install)
/// for account and container identities.
/// </summary>
public class ToolLauncher(
    ILaunchFacade launchFacade,
    AccountToolResolver toolResolver,
    IPackageInstallService packageInstallService,
    ILaunchFeedbackPresenter launchFeedbackPresenter,
    ILoggingService log)
{
    public void OpenCmd(LaunchIdentity identity)
    {
        if (identity is AppContainerLaunchIdentity)
        {
            var cmdExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
            RunWithErrorHandling("Launch CMD",
                () =>
                {
                    using var launch = launchFacade.LaunchFile(new ProcessLaunchTarget(cmdExe), identity, permissionPrompt: (_, _) => true);
                    launchFeedbackPresenter.ShowMaintenanceWarning(launch, new LaunchFeedbackContext("Command Prompt", LaunchFeedbackSource.InteractiveUi));
                });
        }
        else
        {
            var terminalExe = toolResolver.ResolveTerminalExe(identity.Sid);
            var profilePath = toolResolver.GetProfileRoot(identity.Sid);
            var isWt = !terminalExe.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase);
            var launchIdentity = isWt && identity is AccountLaunchIdentity { PrivilegeLevel: null } acctIdentity
                ? acctIdentity with { PrivilegeLevel = PrivilegeLevel.Basic }
                : identity;
            RunWithErrorHandling("Launch CMD", () =>
            {
                try
                {
                    using var launch = launchFacade.LaunchFile(new ProcessLaunchTarget(terminalExe, WorkingDirectory: profilePath), launchIdentity, permissionPrompt: (_, _) => true);
                    launchFeedbackPresenter.ShowMaintenanceWarning(launch, new LaunchFeedbackContext("The terminal", LaunchFeedbackSource.InteractiveUi)
                    {
                        SummaryName = Path.GetFileName(terminalExe)
                    });
                }
                catch (Exception ex) when (isWt && ex is not OperationCanceledException && ex is not GrantOperationException)
                {
                    log.Error($"Launch {terminalExe} failed, falling back to cmd.exe", ex);
                    using var launch = launchFacade.LaunchFile(new ProcessLaunchTarget("cmd.exe", WorkingDirectory: profilePath), identity, permissionPrompt: (_, _) => true);
                    launchFeedbackPresenter.ShowMaintenanceWarning(launch, new LaunchFeedbackContext("Command Prompt", LaunchFeedbackSource.InteractiveUi));
                }
            });
        }
    }

    public void OpenFolderBrowser(LaunchIdentity identity, Func<string, string, bool>? permissionPrompt = null)
    {
        RunWithErrorHandling("Launch Folder Browser", () =>
        {
            using var launch = launchFacade.LaunchFolderBrowser(identity, folderPermissionPrompt: permissionPrompt);
            launchFeedbackPresenter.ShowMaintenanceWarning(launch, new LaunchFeedbackContext("The folder browser", LaunchFeedbackSource.InteractiveUi));
        });
    }

    public void OpenEnvironmentVariables(LaunchIdentity identity)
    {
        RunWithErrorHandling("Launch Environment Variables", () =>
        {
            using var launch = launchFacade.LaunchFile(
                new ProcessLaunchTarget("rundll32.exe", Arguments: "sysdm.cpl,EditEnvironmentVariables"),
                identity,
                permissionPrompt: (_, _) => true);
            launchFeedbackPresenter.ShowMaintenanceWarning(launch, new LaunchFeedbackContext("Environment Variables", LaunchFeedbackSource.InteractiveUi));
        });
    }

    public void InstallPackage(InstallablePackage package, AccountLaunchIdentity identity)
    {
        RunWithErrorHandling($"Install {package.DisplayName}",
            () => launchFeedbackPresenter.ShowMaintenanceWarning(
                packageInstallService.InstallPackages([package], identity),
                new LaunchFeedbackContext("The package installer", LaunchFeedbackSource.InteractiveUi)));
    }

    public void InstallPackages(IReadOnlyList<InstallablePackage> packages, AccountLaunchIdentity identity)
    {
        RunWithErrorHandling(
            "Install packages",
            () => launchFeedbackPresenter.ShowMaintenanceWarning(
                packageInstallService.InstallPackages(packages, identity),
                new LaunchFeedbackContext("The package installer", LaunchFeedbackSource.InteractiveUi)));
    }

    public bool IsWindowsTerminal(string sid) => !toolResolver.ResolveTerminalExe(sid).Equals("cmd.exe", StringComparison.OrdinalIgnoreCase);

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
        catch (GrantOperationException ex)
        {
            launchFeedbackPresenter.ShowGrantFailure(ex, new LaunchFeedbackContext(label, LaunchFeedbackSource.InteractiveUi));
        }
        catch (Exception ex)
        {
            log.Error($"{label} failed", ex);
            MessageBox.Show($"{label} failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

}
