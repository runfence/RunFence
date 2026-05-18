using RunFence.Account;
using RunFence.Acl;
using RunFence.Acl.UI;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Infrastructure;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;

namespace RunFence.Apps.UI;

/// <summary>
/// Orchestrates context menu actions for the ApplicationsPanel app grid.
/// Callers read the selected <see cref="AppEntry"/> from the grid and pass it as a parameter.
/// </summary>
public class AppContextMenuOrchestrator(
    ILaunchFacade facade,
    ISidNameCacheService sidNameCache,
    ILaunchFeedbackPresenter launchFeedbackPresenter,
    ILoggingService log,
    IInteractiveUserDesktopProvider interactiveUserDesktopProvider,
    IShortcutService shortcutService,
    IShellHelper shellHelper,
    DefaultBrowserManager defaultBrowserManager)
{
    public event Action<string>? AccountNavigationRequested;

    /// <summary>
    /// Fired when an action has modified state that must be reflected in the grid.
    /// Persistence is performed by the action itself when required.
    /// </summary>
    public event Action? DataSaveAndRefreshRequested;

    public string? LastShortcutSaveDir { get; set; }

    public void GoToAccount(AppEntry app) => AccountNavigationRequested?.Invoke(app.AccountSid);

    public void OpenInFolderBrowser(AppEntry app, IWin32Window? owner)
    {
        // Capture modifier state before any dialogs that might cause the user to release Shift
        var shiftHeld = (Control.ModifierKeys & Keys.Shift) != 0;

        var parentDir = Path.GetDirectoryName(app.ExePath);
        if (string.IsNullOrEmpty(parentDir))
            return;

        var privilegeLevel = shiftHeld ? PrivilegeLevel.HighestAllowed : app.PrivilegeLevel;
        try
        {
            using var launch = facade.LaunchFolderBrowser(
                new AccountLaunchIdentity(app.AccountSid)
                {
                    PrivilegeLevel = privilegeLevel,
                },
                parentDir,
                folderPermissionPrompt: AclPermissionDialogHelper.CreateLaunchPermissionPrompt(sidNameCache, owner));
            launchFeedbackPresenter.ShowMaintenanceWarning(launch, new LaunchFeedbackContext("The folder browser", LaunchFeedbackSource.InteractiveUi)
            {
                Owner = owner,
                SummaryName = app.Name
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (GrantOperationException ex)
        {
            launchFeedbackPresenter.ShowGrantFailure(ex, new LaunchFeedbackContext("The folder browser", LaunchFeedbackSource.InteractiveUi)
            {
                Owner = owner,
                SummaryName = app.Name
            });
        }
        catch (Exception ex)
        {
            log.Error($"Failed to open folder browser for {app.Name}", ex);
            MessageBox.Show($"Failed to open folder browser: {ex.Message}",
                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public void OpenFolder(AppEntry app)
    {
        if (!app.IsFolder)
            return;
        try
        {
            shellHelper.OpenInExplorer(app.ExePath);
        }
        catch (Exception ex)
        {
            log.Error($"Failed to open folder {app.ExePath}", ex);
            MessageBox.Show($"Failed to open folder: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public void CopyPath(AppEntry app) => Clipboard.SetText(app.ExePath);

    public void OpenDir(AppEntry app)
    {
        if (app.IsFolder)
            return;
        var dir = Path.GetDirectoryName(app.ExePath);
        if (dir != null && Directory.Exists(dir))
        {
            try
            {
                shellHelper.OpenInExplorer(dir);
            }
            catch (Exception ex)
            {
                log.Error($"Failed to open directory {dir}", ex);
                MessageBox.Show($"Failed to open directory: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    public void SaveShortcut(AppEntry app, IWin32Window? owner)
    {
        using var dlg = new SaveFileDialog();
        dlg.Filter = "Shortcut (*.lnk)|*.lnk";
        dlg.FileName = $"{app.Name}.lnk";
        dlg.Title = "Save Shortcut";

        var initialDir = LastShortcutSaveDir ?? interactiveUserDesktopProvider.GetDesktopPath();
        if (initialDir != null && Directory.Exists(initialDir))
            dlg.InitialDirectory = initialDir;
        FileDialogHelper.AddInteractiveUserCustomPlaces(dlg);

        if (dlg.ShowDialog(owner) != DialogResult.OK)
            return;

        LastShortcutSaveDir = Path.GetDirectoryName(dlg.FileName);

        try
        {
            shortcutService.SaveShortcut(app, dlg.FileName);
        }
        catch (Exception ex)
        {
            log.Error($"Failed to save shortcut for {app.Name}", ex);
            MessageBox.Show($"Failed to save shortcut: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public void CopyLauncherPath(AppEntry app)
    {
        var launcherPath = Path.Combine(AppContext.BaseDirectory, PathConstants.LauncherExeName);
        Clipboard.SetText($"\"{launcherPath}\" {app.Id}");
    }

    public void SetDefaultBrowser(AppEntry app)
    {
        var message = defaultBrowserManager.SetDefaultBrowser(app);
        if (message != null)
            MessageBox.Show(message, "Default Browser", MessageBoxButtons.OK, MessageBoxIcon.Information);
        DataSaveAndRefreshRequested?.Invoke();
    }
}
