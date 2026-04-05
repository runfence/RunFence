using System.Security.AccessControl;
using RunFence.Acl.Permissions;
using RunFence.Acl.UI;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Persistence;

namespace RunFence.Apps.UI;

/// <summary>
/// Orchestrates context menu actions for the ApplicationsPanel app grid.
/// Callers read the selected <see cref="AppEntry"/> from the grid and pass it as a parameter.
/// </summary>
public class AppContextMenuOrchestrator(
    IDatabaseProvider databaseProvider,
    ISessionProvider sessionProvider,
    IAppLaunchOrchestrator launchOrchestrator,
    ILoggingService log,
    IInteractiveUserDesktopProvider interactiveUserDesktopProvider,
    IShortcutService shortcutService,
    IPermissionGrantService permissionGrantService,
    IAppHandlerRegistrationService handlerRegistrationService,
    IHandlerMappingService handlerMappingService)
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

        var database = databaseProvider.GetDatabase();
        var session = sessionProvider.GetSession();
        var folderBrowserExe = PathHelper.ResolveExePath(database.Settings.FolderBrowserExePath);
        var cred = session.CredentialStore.Credentials.FirstOrDefault(c =>
            string.Equals(c.Sid, app.AccountSid, StringComparison.OrdinalIgnoreCase));
        if (cred?.IsCurrentAccount != true)
        {
            var confirmFn = PermissionGrantService.AdaptConfirm(path =>
                AclPermissionDialogHelper.ShowPermissionDialog(
                    owner, "Missing permissions",
                    $"The account needs access to:\n{path}"));

            try
            {
                bool grantsAdded = false;
                if (!string.IsNullOrEmpty(folderBrowserExe) && File.Exists(folderBrowserExe))
                    grantsAdded |= permissionGrantService.EnsureExeDirectoryAccess(folderBrowserExe, app.AccountSid, confirmFn).DatabaseModified;
                grantsAdded |= permissionGrantService.EnsureAccess(parentDir, app.AccountSid,
                    FileSystemRights.ReadAndExecute, confirmFn).DatabaseModified;
                if (grantsAdded)
                    DataSaveAndRefreshRequested?.Invoke();
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        var useSplitToken = shiftHeld ? false : app.RunAsSplitToken;
        var launchAsLowIntegrity = shiftHeld ? false : app.LaunchAsLowIntegrity;
        try
        {
            launchOrchestrator.LaunchFolderBrowser(app.AccountSid, parentDir, launchAsLowIntegrity, useSplitToken);
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
            ShellHelper.OpenInExplorer(app.ExePath);
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
                ShellHelper.OpenInExplorer(dir);
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
        var database = databaseProvider.GetDatabase();
        var effectiveMappings = handlerMappingService.GetEffectiveHandlerMappings(database);
        var browserKeys = new[] { "http", "https", ".htm", ".html" };

        var isCurrentDefault = browserKeys.All(key =>
            effectiveMappings.TryGetValue(key, out var mappedId) &&
            string.Equals(mappedId, app.Id, StringComparison.Ordinal));

        if (isCurrentDefault)
        {
            foreach (var key in browserKeys)
                handlerMappingService.RemoveHandlerMapping(key, database);
            var updatedEffective = handlerMappingService.GetEffectiveHandlerMappings(database);
            handlerRegistrationService.Sync(updatedEffective, database.Apps);
        }
        else
        {
            if (!app.AllowPassingArguments)
                app.AllowPassingArguments = true;

            if (string.IsNullOrEmpty(app.ArgumentsTemplate))
                app.ArgumentsTemplate = "\"%1\"";

            foreach (var key in browserKeys)
                handlerMappingService.SetHandlerMapping(key, app.Id, database);

            var updatedEffective = handlerMappingService.GetEffectiveHandlerMappings(database);
            handlerRegistrationService.Sync(updatedEffective, database.Apps);

            MessageBox.Show(
                $"Registered as \"{Constants.HandlerRegisteredAppName}\".\n\n" +
                "The Default Apps settings will now open. Find \"RunFence\" in the browser list and set it as default.",
                "Default Browser", MessageBoxButtons.OK, MessageBoxIcon.Information);
            try
            {
                ShellHelper.OpenDefaultAppsSettings();
            }
            catch (Exception ex)
            {
                log.Warn($"Failed to open Default Apps settings: {ex.Message}");
            }
        }

        DataSaveAndRefreshRequested?.Invoke();
    }
}