using RunFence.Account;
using RunFence.Acl.UI;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
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
    ILoggingService log,
    IInteractiveUserDesktopProvider interactiveUserDesktopProvider,
    IShortcutService shortcutService,
    ShellHelper shellHelper,
    DefaultBrowserManager defaultBrowserManager)
{
    public event Action<string>? AccountNavigationRequested;

    /// <summary>
    /// Fired when an action has modified in-memory app or database data that must be persisted and
    /// reflected in the grid (e.g. grants added in <see cref="OpenInFolderBrowser"/>, or app args
    /// changed in <see cref="SetDefaultBrowser"/>). Subscriber is responsible for saving all configs
    /// and refreshing the grid.
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

        var privilegeLevel = shiftHeld ? (PrivilegeLevel?)PrivilegeLevel.HighestAllowed : app.PrivilegeLevel;
        try
        {
            facade.LaunchFolderBrowser(
                new AccountLaunchIdentity(app.AccountSid)
                {
                    PrivilegeLevel = privilegeLevel,
                },
                parentDir,
                folderPermissionPrompt: AclPermissionDialogHelper.CreateLaunchPermissionPrompt(sidNameCache, owner));
        }
        catch (OperationCanceledException)
        {
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
        var launcherPath = Path.Combine(AppContext.BaseDirectory, Constants.LauncherExeName);
        Clipboard.SetText($"\"{launcherPath}\" {app.Id}");
    }

    public void SetDefaultBrowser(AppEntry app)
    {
        defaultBrowserManager.SetDefaultBrowser(app);
        DataSaveAndRefreshRequested?.Invoke();
    }
}
