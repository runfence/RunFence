using System.ComponentModel;
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
    IAppLaunchOrchestrator launchOrchestrator,
    ILoggingService log)
{
    public void LaunchApp(AppEntry app)
    {
        try
        {
            launchOrchestrator.Launch(app, null);
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
        catch (Exception ex)
        {
            log.Error($"Failed to launch tray app {app.Name}", ex);
            MessageBox.Show($"Failed to launch: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public void LaunchFolderBrowser(string accountSid, bool shiftHeld)
    {
        try
        {
            launchOrchestrator.LaunchFolderBrowserFromTray(accountSid,
                msg => AclPermissionDialogHelper.ShowPermissionDialog(null, "Missing permissions", msg),
                useSplitToken: shiftHeld ? false : null,
                useLowIntegrity: shiftHeld ? false : null);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            log.Error("Failed to launch tray folder browser", ex);
            MessageBox.Show($"Failed to open folder browser: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public void LaunchTerminal(string accountSid, bool shiftHeld)
    {
        try
        {
            launchOrchestrator.LaunchTerminalFromTray(accountSid,
                useSplitToken: shiftHeld ? false : null,
                useLowIntegrity: shiftHeld ? false : null);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            log.Error("Failed to launch tray terminal", ex);
            MessageBox.Show($"Failed to open terminal: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public void LaunchDiscoveredApp(string exePath, string accountSid)
    {
        try
        {
            launchOrchestrator.LaunchDiscoveredApp(exePath, accountSid);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            log.Error("Failed to launch discovered app", ex);
            MessageBox.Show($"Failed to launch app: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}