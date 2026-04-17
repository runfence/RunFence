using System.ComponentModel;
using RunFence.Account;
using RunFence.Account.UI;
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
    ISidNameCacheService sidNameCache,
    ILoggingService log)
{
    public void LaunchApp(AppEntry app)
        => RunWithLaunchErrorHandling(
            () => entryLauncher.Launch(app, null,
                permissionPrompt: AclPermissionDialogHelper.CreateLaunchPermissionPrompt(sidNameCache)),
            $"tray app {app.Name}");

    public void LaunchFolderBrowser(LaunchIdentity identity)
        => RunWithLaunchErrorHandling(
            () => launchService.OpenFolderBrowser(identity,
                AclPermissionDialogHelper.CreateLaunchPermissionPrompt(sidNameCache)),
            "folder browser");

    public void LaunchTerminal(LaunchIdentity identity)
        => RunWithLaunchErrorHandling(
            () => launchService.OpenCmd(identity),
            "terminal");

    public void LaunchDiscoveredApp(string exePath, LaunchIdentity identity)
        => RunWithLaunchErrorHandling(
            () => facade.LaunchFile(new ProcessLaunchTarget(exePath), identity),
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
        catch (Exception ex)
        {
            log.Error($"Failed to launch {context}", ex);
            MessageBox.Show($"Failed to launch: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
