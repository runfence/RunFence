using System.ComponentModel;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Acl;
using RunFence.Acl.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launch;

namespace RunFence.UI;

/// <summary>
/// Handles tray icon launch events: app launch, folder browser, terminal, and discovered app.
/// Extracted from MainForm to keep launch logic separate from form lifecycle management.
/// </summary>
public class TrayLaunchHandler(
    IAppEntryLauncher entryLauncher,
    ILaunchFacade facade,
    ToolLauncher launchService,
    ILaunchFeedbackPresenter launchFeedbackPresenter,
    ISidNameCacheService sidNameCache,
    ILoggingService log)
{
    public void LaunchApp(AppEntry app)
        => RunWithLaunchErrorHandling(
            () =>
            {
                using var launch = entryLauncher.Launch(app, null,
                    permissionPrompt: AclPermissionDialogHelper.CreateLaunchPermissionPrompt(sidNameCache));
                launchFeedbackPresenter.ShowMaintenanceWarning(launch, new LaunchFeedbackContext("The application", LaunchFeedbackSource.InteractiveUi)
                {
                    SummaryName = app.Name
                });
            },
            $"tray app {app.Name}");

    public void LaunchFolderBrowser(LaunchIdentity identity)
        => RunWithLaunchErrorHandling(
            () => launchService.OpenFolderBrowser(identity,
                AclPermissionDialogHelper.CreateLaunchPermissionPrompt(sidNameCache)),
            "folder browser");

    public Task LaunchTerminalAsync(LaunchIdentity identity)
        => RunWithLaunchErrorHandlingAsync(
            async () =>
            {
                await launchService.OpenCmdAsync(identity, requestTerminalRefresh: identity is AccountLaunchIdentity);
            },
            "terminal");

    public void LaunchDiscoveredApp(string exePath, LaunchIdentity identity)
        => RunWithLaunchErrorHandling(
            () =>
            {
                using var launch = facade.LaunchFile(new ProcessLaunchTarget(exePath, IsPathApproved: false), identity);
                launchFeedbackPresenter.ShowMaintenanceWarning(launch, new LaunchFeedbackContext("The application", LaunchFeedbackSource.InteractiveUi)
                {
                    SummaryName = Path.GetFileName(exePath)
                });
            },
            "discovered app");

    private void RunWithLaunchErrorHandling(Action launchAction, string context)
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
        catch (Win32Exception ex) when (ex.NativeErrorCode == ProcessLaunchNative.Win32ErrorLogonFailure)
        {
            MessageBox.Show("Stored credentials are incorrect. Please update the password in RunFence.",
                "Launch Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (OperationCanceledException)
        {
        }
        catch (GrantOperationException ex)
        {
            launchFeedbackPresenter.ShowGrantFailure(ex, new LaunchFeedbackContext(context, LaunchFeedbackSource.InteractiveUi));
        }
        catch (Exception ex)
        {
            log.Error($"Failed to launch {context}", ex);
            MessageBox.Show($"Failed to launch: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task RunWithLaunchErrorHandlingAsync(Func<Task> launchAction, string context)
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
        catch (Win32Exception ex) when (ex.NativeErrorCode == ProcessLaunchNative.Win32ErrorLogonFailure)
        {
            MessageBox.Show("Stored credentials are incorrect. Please update the password in RunFence.",
                "Launch Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (OperationCanceledException)
        {
        }
        catch (GrantOperationException ex)
        {
            launchFeedbackPresenter.ShowGrantFailure(ex, new LaunchFeedbackContext(context, LaunchFeedbackSource.InteractiveUi));
        }
        catch (Exception ex)
        {
            log.Error($"Failed to launch {context}", ex);
            MessageBox.Show($"Failed to launch: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
