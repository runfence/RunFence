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
    IWindowsTerminalAccountStateService windowsTerminalAccountStateService,
    TerminalLaunchIdentitySelector terminalLaunchIdentitySelector,
    IPackageInstallService packageInstallService,
    IWindowsTerminalDeploymentProgressRunner windowsTerminalDeploymentProgressRunner,
    IWindowsTerminalLaunchRefreshService windowsTerminalLaunchRefreshService,
    ILaunchFeedbackPresenter launchFeedbackPresenter,
    ILoggingService log)
{
    public Task OpenCmdAsync(LaunchIdentity identity, bool requestTerminalRefresh = false, IWin32Window? owner = null)
    {
        if (identity is AppContainerLaunchIdentity)
        {
            var cmdExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
            return RunWithErrorHandlingAsync("Launch CMD",
                () =>
                {
                    using var launch = launchFacade.LaunchFile(new ProcessLaunchTarget(cmdExe), identity, permissionPrompt: (_, _) => true);
                    launchFeedbackPresenter.ShowMaintenanceWarning(launch, new LaunchFeedbackContext("Command Prompt", LaunchFeedbackSource.InteractiveUi)
                    {
                        Owner = owner
                    });
                    return Task.CompletedTask;
                });
        }
        else
        {
            var originalAccountIdentity = (AccountLaunchIdentity)identity;
            return RunWithErrorHandlingAsync(
                "Launch CMD",
                () => OpenTerminalForAccountAsync(originalAccountIdentity, requestTerminalRefresh, owner));
        }
    }

    public async Task<TerminalLaunchStatus> OpenTerminalForAccountAsync(
        AccountLaunchIdentity launchIdentity,
        bool requestTerminalRefresh = false,
        IWin32Window? owner = null)
    {
        await windowsTerminalLaunchRefreshService.EnsureSharedDeploymentExistsBeforeTerminalLaunchAsync(launchIdentity);
        var terminalExe = windowsTerminalAccountStateService.ResolveLaunchTarget(launchIdentity);
        var profilePath = toolResolver.GetProfileRoot(launchIdentity.Sid);
        var isWt = !terminalExe.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase);
        var resolvedLaunchIdentity = terminalLaunchIdentitySelector.ResolveLaunchIdentity(launchIdentity, terminalExe);

        try
        {
            var target = new ProcessLaunchTarget(terminalExe, WorkingDirectory: profilePath);
            using var launch = launchFacade.LaunchFile(target, resolvedLaunchIdentity, permissionPrompt: (_, _) => true);
            var status = ShowTerminalLaunchFeedback(
                launch,
                "The terminal",
                Path.GetFileName(terminalExe),
                owner,
                launchIdentity,
                requestTerminalRefresh);
            return status;
        }
        catch (Exception ex) when (isWt && ex is not OperationCanceledException && ex is not GrantOperationException)
        {
            log.Error($"Launch {terminalExe} failed, falling back to cmd.exe", ex);
            using var launch = launchFacade.LaunchFile(
                new ProcessLaunchTarget("cmd.exe", WorkingDirectory: profilePath),
                launchIdentity,
                permissionPrompt: (_, _) => true);
            return ShowTerminalLaunchFeedback(
                launch,
                "Command Prompt",
                null,
                owner,
                launchIdentity,
                requestTerminalRefresh);
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

    public Task InstallPackageAsync(InstallablePackage package, AccountLaunchIdentity identity)
        => InstallPackagesAsync([package], identity, $"Install {package.DisplayName}");

    public Task InstallPackagesAsync(IReadOnlyList<InstallablePackage> packages, AccountLaunchIdentity identity)
        => InstallPackagesAsync(packages, identity, "Install packages");

    private async Task InstallPackagesAsync(
        IReadOnlyList<InstallablePackage> packages,
        AccountLaunchIdentity identity,
        string label)
    {
        await RunWithErrorHandlingAsync(label, async () =>
        {
            var packagesToInstall = KnownPackages.ExpandWithDependencies(packages);
            var warnings = packagesToInstall.Contains(KnownPackages.WindowsTerminal)
                ? await windowsTerminalDeploymentProgressRunner.RunAsync(
                    "Preparing shared Windows Terminal deployment...",
                    cancellationToken => packageInstallService.InstallPackagesAsync(packages, identity, cancellationToken))
                : await packageInstallService.InstallPackagesAsync(packages, identity, CancellationToken.None);

            launchFeedbackPresenter.ShowMaintenanceWarning(
                warnings,
                new LaunchFeedbackContext("The package installer", LaunchFeedbackSource.InteractiveUi));
        });
    }

    public bool IsWindowsTerminal(string sid) => !windowsTerminalAccountStateService.ResolveLaunchTarget(sid).Equals("cmd.exe", StringComparison.OrdinalIgnoreCase);

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

    private async Task RunWithErrorHandlingAsync(string label, Func<Task> launchAction)
    {
        try
        {
            await launchAction();
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

    private TerminalLaunchStatus ShowTerminalLaunchFeedback(
        LaunchExecutionResult launch,
        string startedItem,
        string? summaryName,
        IWin32Window? owner,
        AccountLaunchIdentity launchIdentity,
        bool requestTerminalRefresh)
    {
        launchFeedbackPresenter.ShowMaintenanceWarning(launch, new LaunchFeedbackContext(startedItem, LaunchFeedbackSource.InteractiveUi)
        {
            Owner = owner,
            SummaryName = summaryName
        });

        var refreshRequested = false;
        if (requestTerminalRefresh)
        {
            windowsTerminalLaunchRefreshService.TryStartOnlineRefreshAfterTerminalLaunch(launchIdentity);
            refreshRequested = true;
        }

        return new TerminalLaunchStatus(startedItem, summaryName, refreshRequested);
    }

}
