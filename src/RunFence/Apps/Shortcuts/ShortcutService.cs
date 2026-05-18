using System.Runtime.ExceptionServices;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Apps.Shortcuts;

public class ShortcutService(
    ILoggingService log,
    IIconService iconService,
    IShortcutProtectionService protection,
    IShortcutWriteAccessService shortcutWriteAccessService,
    IShortcutComHelper shortcutHelper,
    IInteractiveUserDesktopProvider interactiveUserDesktopProvider,
    ShortcutFinder finder)
    : IShortcutService
{
    public void ReplaceShortcuts(AppEntry app, string launcherPath, string iconPath, ShortcutTraversalCache cache)
    {
        var shortcuts = app.IsFolder
            ? finder.FindShortcutsForFolder(app.ExePath, cache)
            : finder.FindShortcutsForExe(app.ExePath, cache);

        ReplaceShortcutsFromList(app, launcherPath, iconPath, shortcuts, cache);

        var (_, byLauncherAppId) = finder.ScanAllShortcuts(cache);
        if (byLauncherAppId.TryGetValue(app.Id, out var managedShortcuts))
            EnforceManagedShortcuts(app, launcherPath, iconPath, managedShortcuts, cache);
    }

    public void SaveShortcut(AppEntry app, string shortcutPath)
    {
        var launcherPath = Path.Combine(AppContext.BaseDirectory, PathConstants.LauncherExeName);
        var iconPath = iconService.GetIconPath(app.Id);
        shortcutWriteAccessService.Save(shortcutPath, new ShortcutMutation(
                launcherPath,
                app.Id,
                ShortcutPathHelper.GetLauncherWorkingDirectory(launcherPath),
                File.Exists(iconPath) ? $"{iconPath},0" : null,
                File.Exists(iconPath) ? ShortcutIconUpdateMode.Set : ShortcutIconUpdateMode.None,
                null,
                null,
                1),
            ShortcutDestinationMetadataMode.PreserveExisting,
            ShortcutContentMode.RecreateCanonical);

        if (File.Exists(shortcutPath) && !IsTaskBarPath(shortcutPath))
            protection.ProtectShortcut(shortcutPath);
    }

    public void RevertShortcuts(AppEntry app, ShortcutTraversalCache cache)
    {
        var shortcuts = finder.FindShortcutsForLauncher(app.Id, cache);
        List<string>? warnings = null;
        List<Exception>? warningCauses = null;

        foreach (var shortcutPath in shortcuts)
        {
            try
            {
                var result = RevertSingleShortcutCore(shortcutPath, app);
                if (result != null)
                    cache.RecordShortcut(result.Value.Path, result.Value.TargetPath, result.Value.Arguments);
            }
            catch (ShortcutProtectionException)
            {
                throw;
            }
            catch (Exception ex)
            {
                log.Warn($"Failed to revert shortcut for {app.Name}: {shortcutPath} | {ex.GetType().Name}: {ex.Message}");
                (warnings ??= []).Add($"{shortcutPath}: {ex.Message}");
                (warningCauses ??= []).Add(ex);
            }
        }

        if (warnings is { Count: > 0 })
        {
            throw new ShortcutEnforcementException(
                "Failed to revert shortcut changes:\n\n" + string.Join("\n", warnings),
                warningCauses ?? []);
        }
    }

    public void UpdateShortcutToLauncher(string shortcutPath, string appId, string launcherPath, string? iconPath)
    {
        try
        {
            UpdateShortcutToLauncherCore(shortcutPath, appId, launcherPath, iconPath);
        }
        catch (ShortcutPostWriteException ex)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException!).Throw();
        }
    }

    public bool RevertSingleShortcut(string shortcutPath, AppEntry app)
        => RevertSingleShortcutCore(shortcutPath, app) != null;

    public void EnforceShortcuts(IEnumerable<AppEntry> apps, string launcherPath, ShortcutTraversalCache cache)
    {
        var appList = apps.Where(a => a.ManageShortcuts).ToList();
        if (appList.Count == 0)
            return;

        var (byTarget, byLauncherAppId) = finder.ScanAllShortcuts(cache);
        List<string>? warnings = null;
        List<Exception>? warningCauses = null;

        foreach (var app in appList)
        {
            string normalizedExePath;
            try { normalizedExePath = Path.GetFullPath(app.ExePath); }
            catch { normalizedExePath = app.ExePath; }

            byTarget.TryGetValue(normalizedExePath, out var shortcuts);
            var shortcutList = shortcuts != null ? new List<string>(shortcuts) : new List<string>();

            if (app.IsFolder)
                shortcutList = finder.FindShortcutsForFolder(app.ExePath, cache);

            byLauncherAppId.TryGetValue(app.Id, out var existingManaged);
            var managedList = existingManaged ?? [];
            var managedPaths = managedList.Select(s => s.Path).ToList();
            var iconPath = iconService.GetIconPath(app.Id);

            bool hasNewShortcuts = shortcutList.Any(s =>
                !managedPaths.Contains(s, StringComparer.OrdinalIgnoreCase));

            if (hasNewShortcuts)
            {
                try
                {
                    ReplaceShortcutsFromList(app, launcherPath, iconPath, shortcutList, cache);
                }
                catch (ShortcutProtectionException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    log.Error($"Failed to enforce shortcut for {app.Name}", ex);
                }
            }

            try
            {
                EnforceManagedShortcuts(app, launcherPath, iconPath, managedList, cache);
            }
            catch (ShortcutEnforcementException ex)
            {
                (warnings ??= []).Add(ex.Message);
                (warningCauses ??= []).Add(ex);
            }
        }

        if (warnings is { Count: > 0 })
        {
            throw new ShortcutEnforcementException(string.Join("\n\n", warnings), warningCauses ?? []);
        }
    }

    private void EnforceManagedShortcuts(
        AppEntry app,
        string launcherPath,
        string? iconPath,
        IReadOnlyList<ShortcutFinder.ManagedShortcut> managedList,
        ShortcutTraversalCache cache)
    {
        List<string>? warnings = null;
        List<Exception>? warningCauses = null;

        foreach (var managedShortcut in managedList)
        {
            var shortcutPath = managedShortcut.Path;
            if (ManagedShortcutNeedsRepair(managedShortcut, launcherPath))
            {
                try
                {
                    var existingIconPath = !string.IsNullOrEmpty(iconPath) && File.Exists(iconPath)
                        ? iconPath
                        : null;
                    var result = UpdateExistingManagedShortcutToCurrentLauncher(
                        shortcutPath, launcherPath, existingIconPath, managedShortcut.TargetPath);
                    cache.RecordShortcut(result.Path, result.TargetPath, result.Arguments);
                }
                catch (ShortcutPostWriteException ex)
                {
                    cache.RecordShortcut(ex.Result.Path, ex.Result.TargetPath, ex.Result.Arguments);
                    ExceptionDispatchInfo.Capture(ex.InnerException!).Throw();
                }
                catch (ShortcutProtectionException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    log.Error($"Failed to repair managed shortcut launcher path for {app.Name}: {shortcutPath}", ex);
                    (warnings ??= []).Add($"{shortcutPath}: {ex.Message}");
                    (warningCauses ??= []).Add(ex);
                }

                continue;
            }

            if (File.Exists(shortcutPath) && !IsTaskBarPath(shortcutPath))
                protection.ProtectShortcut(shortcutPath);
        }

        if (warnings is { Count: > 0 })
        {
            throw new ShortcutEnforcementException(
                "Failed to repair managed shortcut launcher path:\n\n" + string.Join("\n", warnings),
                warningCauses ?? []);
        }
    }

    private ShortcutWriteResult UpdateShortcutToLauncherCore(
        string shortcutPath,
        string appId,
        string launcherPath,
        string? iconPath)
    {
        string? finalArguments;

        var currentState = ReadCurrentShortcutState(shortcutPath);
        var currentArgs = currentState.Arguments ?? "";
        finalArguments = string.IsNullOrEmpty(currentArgs)
            ? appId
            : $"{appId} {currentArgs}";
        shortcutWriteAccessService.Save(shortcutPath, new ShortcutMutation(
            launcherPath,
            finalArguments,
            ShortcutPathHelper.GetLauncherWorkingDirectory(launcherPath),
            !string.IsNullOrEmpty(iconPath) && File.Exists(iconPath)
                ? $"{iconPath},0"
                : currentState.IconLocation,
            !string.IsNullOrEmpty(iconPath) && File.Exists(iconPath) ? ShortcutIconUpdateMode.Set : ShortcutIconUpdateMode.None,
            currentState.Description,
            currentState.Hotkey,
            currentState.WindowStyle),
            ShortcutDestinationMetadataMode.PreserveExisting,
            ShortcutContentMode.PreserveExisting);

        var result = new ShortcutWriteResult(shortcutPath, launcherPath, finalArguments);
        if (!IsTaskBarPath(shortcutPath))
        {
            try
            {
                protection.ProtectShortcut(shortcutPath);
            }
            catch (Exception ex)
            {
                throw new ShortcutPostWriteException(result, ex);
            }
        }

        log.Info($"Updated shortcut to launcher: {shortcutPath}");
        return result;
    }

    private ShortcutWriteResult? RevertSingleShortcutCore(string shortcutPath, AppEntry app)
    {
        var currentState = ReadCurrentShortcutState(shortcutPath);
        var parsedArgs = ShortcutClassificationHelper.ParseManagedShortcutArgs(currentState.Arguments ?? "", app.Id);

        if (parsedArgs == null)
        {
            log.Warn($"Shortcut {shortcutPath} has unexpected args, cannot revert: {currentState.Arguments}");
            return null;
        }

        shortcutWriteAccessService.Save(shortcutPath, new ShortcutMutation(
            app.ExePath,
            parsedArgs!,
            !string.IsNullOrWhiteSpace(app.WorkingDirectory)
                ? app.WorkingDirectory
                : Path.GetDirectoryName(app.ExePath) ?? "",
            currentState.IconLocation,
            ShortcutIconUpdateMode.ClearBestEffort,
            currentState.Description,
            currentState.Hotkey,
            currentState.WindowStyle),
            ShortcutDestinationMetadataMode.ResetForRecreatedShortcut,
            ShortcutContentMode.PreserveExisting);

        log.Info($"Reverted shortcut: {shortcutPath}");
        return new ShortcutWriteResult(shortcutPath, app.ExePath, parsedArgs);
    }

    private ShortcutWriteResult UpdateExistingManagedShortcutToCurrentLauncher(
        string shortcutPath,
        string launcherPath,
        string? iconPath,
        string oldTargetPath)
    {
        var currentState = ReadCurrentShortcutState(shortcutPath);
        var currentWorkingDirectory = currentState.WorkingDirectory;
        string right = ShortcutPathHelper.GetLauncherWorkingDirectory(oldTargetPath);
        var updatedWorkingDirectory = PathHelper.IsSamePath(currentWorkingDirectory ?? "", right)
            ? ShortcutPathHelper.GetLauncherWorkingDirectory(launcherPath)
            : currentWorkingDirectory;
        shortcutWriteAccessService.Save(shortcutPath, new ShortcutMutation(
            launcherPath,
            currentState.Arguments,
            updatedWorkingDirectory,
            !string.IsNullOrEmpty(iconPath) && File.Exists(iconPath)
                ? $"{iconPath},0"
                : currentState.IconLocation,
            !string.IsNullOrEmpty(iconPath) && File.Exists(iconPath) ? ShortcutIconUpdateMode.Set : ShortcutIconUpdateMode.None,
            currentState.Description,
            currentState.Hotkey,
            currentState.WindowStyle),
            ShortcutDestinationMetadataMode.PreserveExisting,
            ShortcutContentMode.PreserveExisting);

        var result = new ShortcutWriteResult(shortcutPath, launcherPath, currentState.Arguments);
        if (!IsTaskBarPath(shortcutPath))
        {
            try
            {
                protection.ProtectShortcut(shortcutPath);
            }
            catch (Exception ex)
            {
                throw new ShortcutPostWriteException(result, ex);
            }
        }

        log.Info($"Updated managed shortcut launcher path: {shortcutPath}");
        return result;
    }

    private bool ManagedShortcutNeedsRepair(ShortcutFinder.ManagedShortcut managedShortcut, string launcherPath)
        => !PathHelper.IsSamePath(managedShortcut.TargetPath, launcherPath);

    private bool IsTaskBarPath(string shortcutPath)
    {
        var taskBarFolder = interactiveUserDesktopProvider.GetTaskBarPath();
        if (taskBarFolder == null)
            return false;
        try
        {
            var normalizedShortcut = Path.GetFullPath(shortcutPath);
            var normalizedFolder = Path.GetFullPath(taskBarFolder);
            return normalizedShortcut.StartsWith(
                normalizedFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private ShortcutWriteResult ReplaceShortcut(string shortcutPath, AppEntry app, string launcherPath, string iconPath)
        => UpdateShortcutToLauncherCore(shortcutPath, app.Id, launcherPath, iconPath);

    private ShortcutMutation ReadCurrentShortcutState(string shortcutPath)
        => shortcutHelper.WithShortcut(shortcutPath, shortcut => new ShortcutMutation(
            (string?)shortcut.TargetPath ?? "",
            (string?)shortcut.Arguments,
            (string?)shortcut.WorkingDirectory,
            (string?)shortcut.IconLocation,
            ShortcutIconUpdateMode.None,
            (string?)shortcut.Description,
            (string?)shortcut.Hotkey,
            (int?)shortcut.WindowStyle ?? 1));

    private void ReplaceShortcutsFromList(
        AppEntry app,
        string launcherPath,
        string iconPath,
        List<string> shortcuts,
        ShortcutTraversalCache cache)
    {
        foreach (var shortcutPath in shortcuts)
        {
            try
            {
                var result = ReplaceShortcut(shortcutPath, app, launcherPath, iconPath);
                cache.RecordShortcut(result.Path, result.TargetPath, result.Arguments);
            }
            catch (ShortcutPostWriteException ex)
            {
                cache.RecordShortcut(ex.Result.Path, ex.Result.TargetPath, ex.Result.Arguments);
                ExceptionDispatchInfo.Capture(ex.InnerException!).Throw();
            }
            catch (ShortcutProtectionException)
            {
                throw;
            }
            catch (Exception ex)
            {
                log.Error($"Failed to replace shortcut: {shortcutPath}", ex);
            }
        }
    }

}
