using System.ComponentModel;
using RunFence.Account;
using RunFence.Acl.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.RunAs;

namespace RunFence.Apps.UI;

/// <summary>
/// Handles launch-related operations for <see cref="Forms.ApplicationsPanel"/>:
/// launching app entries and triggering the RunAs flow.
/// Decouples launch execution and error handling from the panel's grid management concerns.
/// </summary>
public class ApplicationsPanelLaunchHandler(
    AppEntryLauncher entryLauncher,
    ISidNameCacheService sidNameCache,
    ILoggingService log,
    IRunAsFlowHandler runAsFlowHandler)
{
    /// <summary>
    /// Launches the given app entry, showing error dialogs on failure.
    /// </summary>
    public void LaunchApp(AppEntry app, string? launcherArguments, IWin32Window? owner)
    {
        try
        {
            entryLauncher.Launch(app, launcherArguments,
                permissionPrompt: AclPermissionDialogHelper.CreateLaunchPermissionPrompt(sidNameCache, owner));
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
            log.Error($"Launch failed for {app.Name}", ex);
            MessageBox.Show($"Failed to launch: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Triggers the RunAs flow for the given file path.
    /// </summary>
    public void TriggerRunAs(string filePath) => runAsFlowHandler.TriggerFromUI(filePath);
}
