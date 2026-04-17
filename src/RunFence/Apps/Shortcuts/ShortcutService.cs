using System.Runtime.ExceptionServices;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Apps.Shortcuts;

public class ShortcutService(
    ILoggingService log,
    IShortcutProtectionService protection,
    IShortcutComHelper shortcutHelper,
    IInteractiveUserDesktopProvider interactiveUserDesktopProvider)
    : IShortcutService
{
    private readonly record struct ManagedShortcut(string Path, string TargetPath, string? Arguments);

    public void ReplaceShortcuts(AppEntry app, string launcherPath, string iconPath, ShortcutTraversalCache cache)
    {
        var shortcuts = app.IsFolder
            ? FindShortcutsForFolder(app.ExePath, cache)
            : FindShortcutsForExe(app.ExePath, cache);

        ReplaceShortcutsFromList(app, launcherPath, iconPath, shortcuts, cache);

        var (_, byLauncherAppId) = ScanAllShortcuts(cache);
        if (byLauncherAppId.TryGetValue(app.Id, out var managedShortcuts))
            EnforceManagedShortcuts(app, launcherPath, iconPath, managedShortcuts, cache);
    }

    public void SaveShortcut(AppEntry app, string shortcutPath)
    {
        var launcherPath = Path.Combine(AppContext.BaseDirectory, Constants.LauncherExeName);
        var iconPath = Path.Combine(Constants.ProgramDataIconDir, $"{app.Id}.ico");
        shortcutHelper.WithShortcut(shortcutPath, sc =>
        {
            sc.TargetPath = launcherPath;
            sc.Arguments = app.Id;
            sc.WorkingDirectory = ShortcutPathHelper.GetLauncherWorkingDirectory(launcherPath);
            if (File.Exists(iconPath))
                sc.IconLocation = $"{iconPath},0";
            sc.Save();
        });
    }

    public void RevertShortcuts(AppEntry app, ShortcutTraversalCache cache)
    {
        var shortcuts = FindShortcutsForLauncher(app.Id, cache);

        foreach (var shortcutPath in shortcuts)
        {
            try
            {
                var result = RevertSingleShortcutCore(shortcutPath, app);
                if (result != null)
                    cache.RecordShortcut(result.Value.Path, result.Value.TargetPath, result.Value.Arguments);
            }
            catch (Exception ex)
            {
                log.Error($"Failed to revert shortcut: {shortcutPath}", ex);
            }
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

        var (byTarget, byLauncherAppId) = ScanAllShortcuts(cache);

        foreach (var app in appList)
        {
            string normalizedExePath;
            try { normalizedExePath = Path.GetFullPath(app.ExePath); }
            catch { normalizedExePath = app.ExePath; }

            byTarget.TryGetValue(normalizedExePath, out var shortcuts);
            var shortcutList = shortcuts != null ? new List<string>(shortcuts) : new List<string>();

            if (app.IsFolder)
                shortcutList = FindShortcutsForFolder(app.ExePath, cache);

            byLauncherAppId.TryGetValue(app.Id, out var existingManaged);
            var managedList = existingManaged ?? [];
            var managedPaths = managedList.Select(s => s.Path).ToList();
            var iconPath = Path.Combine(Constants.ProgramDataIconDir, $"{app.Id}.ico");

            bool hasNewShortcuts = shortcutList.Any(s =>
                !managedPaths.Contains(s, StringComparer.OrdinalIgnoreCase));

            if (hasNewShortcuts)
            {
                try
                {
                    ReplaceShortcutsFromList(app, launcherPath, iconPath, shortcutList, cache);
                }
                catch (Exception ex)
                {
                    log.Error($"Failed to enforce shortcut for {app.Name}", ex);
                }
            }

            EnforceManagedShortcuts(app, launcherPath, iconPath, managedList, cache);
        }
    }

    private void EnforceManagedShortcuts(
        AppEntry app,
        string launcherPath,
        string? iconPath,
        IReadOnlyList<ManagedShortcut> managedList,
        ShortcutTraversalCache cache)
    {
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
                    log.Error($"Failed to repair managed shortcut launcher path for {app.Name}: {shortcutPath}", ex.InnerException!);
                }
                catch (Exception ex)
                {
                    log.Error($"Failed to repair managed shortcut launcher path for {app.Name}: {shortcutPath}", ex);
                }

                continue;
            }

            if (File.Exists(shortcutPath) && !IsTaskBarPath(shortcutPath))
                protection.ProtectShortcut(shortcutPath);
        }
    }

    private ShortcutWriteResult UpdateShortcutToLauncherCore(
        string shortcutPath,
        string appId,
        string launcherPath,
        string? iconPath)
    {
        string? finalArguments = null;

        protection.UnprotectShortcut(shortcutPath);

        shortcutHelper.WithShortcut(shortcutPath, sc =>
        {
            string currentArgs = sc.Arguments ?? "";

            sc.TargetPath = launcherPath;
            sc.WorkingDirectory = ShortcutPathHelper.GetLauncherWorkingDirectory(launcherPath);
            sc.Arguments = string.IsNullOrEmpty(currentArgs)
                ? appId
                : $"{appId} {currentArgs}";
            finalArguments = sc.Arguments;

            if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                sc.IconLocation = $"{iconPath},0";

            sc.Save();
        });

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
        string? parsedArgs = null;
        string? currentArgsForWarning = null;

        var canRevert = shortcutHelper.WithShortcut(shortcutPath, sc =>
        {
            string currentArgs = sc.Arguments ?? "";
            parsedArgs = ShortcutClassificationHelper.ParseManagedShortcutArgs(currentArgs, app.Id);
            if (parsedArgs == null)
                currentArgsForWarning = currentArgs;
            return parsedArgs != null;
        });

        if (!canRevert)
        {
            log.Warn($"Shortcut {shortcutPath} has unexpected args, cannot revert: {currentArgsForWarning}");
            return null;
        }

        protection.UnprotectShortcut(shortcutPath);

        shortcutHelper.WithShortcut(shortcutPath, sc =>
        {
            sc.TargetPath = app.ExePath;
            sc.Arguments = parsedArgs!;
            try
            {
                sc.IconLocation = "";
            }
            catch
            {
                /* COM 0x80070057 on some systems */
            }

            sc.WorkingDirectory = !string.IsNullOrWhiteSpace(app.WorkingDirectory)
                ? app.WorkingDirectory
                : Path.GetDirectoryName(app.ExePath) ?? "";
            sc.Save();
        });

        log.Info($"Reverted shortcut: {shortcutPath}");
        return new ShortcutWriteResult(shortcutPath, app.ExePath, parsedArgs);
    }

    private ShortcutWriteResult UpdateExistingManagedShortcutToCurrentLauncher(
        string shortcutPath,
        string launcherPath,
        string? iconPath,
        string oldTargetPath)
    {
        string? arguments = null;
        protection.UnprotectShortcut(shortcutPath);

        shortcutHelper.WithShortcut(shortcutPath, sc =>
        {
            var oldWorkingDirectory = (string?)sc.WorkingDirectory;
            sc.TargetPath = launcherPath;
            if (ShortcutPathHelper.IsSamePath(
                    oldWorkingDirectory ?? "",
                    ShortcutPathHelper.GetLauncherWorkingDirectory(oldTargetPath)))
                sc.WorkingDirectory = ShortcutPathHelper.GetLauncherWorkingDirectory(launcherPath);
            arguments = sc.Arguments;
            if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                sc.IconLocation = $"{iconPath},0";
            sc.Save();
        });

        var result = new ShortcutWriteResult(shortcutPath, launcherPath, arguments);
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

    private bool ManagedShortcutNeedsRepair(ManagedShortcut managedShortcut, string launcherPath)
        => !ShortcutPathHelper.IsSamePath(managedShortcut.TargetPath, launcherPath);

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

    private List<string> FindShortcutsForExe(string exePath, ShortcutTraversalCache cache)
    {
        var normalized = Path.GetFullPath(exePath);
        return cache.FindWhere((target, _) =>
        {
            if (target == null)
                return false;
            try
            {
                return string.Equals(Path.GetFullPath(target), normalized, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        });
    }

    private List<string> FindShortcutsForFolder(string folderPath, ShortcutTraversalCache cache)
    {
        var normalizedFolder = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar);
        return cache.FindWhere((target, args) =>
            ShortcutClassificationHelper.IsFolderShortcutTarget(target, args, normalizedFolder));
    }

    private ShortcutWriteResult ReplaceShortcut(string shortcutPath, AppEntry app, string launcherPath, string iconPath)
        => UpdateShortcutToLauncherCore(shortcutPath, app.Id, launcherPath, iconPath);

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
                log.Error($"Failed to replace shortcut: {shortcutPath}", ex.InnerException!);
            }
            catch (Exception ex)
            {
                log.Error($"Failed to replace shortcut: {shortcutPath}", ex);
            }
        }
    }

    private (Dictionary<string, List<string>> byTarget, Dictionary<string, List<ManagedShortcut>> byLauncherAppId)
        ScanAllShortcuts(ShortcutTraversalCache cache)
    {
        var byTarget = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var byLauncherAppId = new Dictionary<string, List<ManagedShortcut>>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in cache.Entries)
        {
            var lnk = entry.Path;
            var target = entry.TargetPath;
            var args = entry.Arguments;
            if (target == null)
                continue;

            string normalizedTarget;
            try { normalizedTarget = Path.GetFullPath(target); }
            catch { normalizedTarget = target; }

            if (!byTarget.TryGetValue(normalizedTarget, out var targetList))
            {
                targetList = [];
                byTarget[normalizedTarget] = targetList;
            }

            targetList.Add(lnk);

            if (target.EndsWith(Constants.LauncherExeName, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(args))
            {
                var spaceIndex = args.IndexOf(' ');
                var appId = spaceIndex < 0 ? args : args[..spaceIndex];

                if (!byLauncherAppId.TryGetValue(appId, out var idList))
                {
                    idList = [];
                    byLauncherAppId[appId] = idList;
                }

                idList.Add(new ManagedShortcut(lnk, target, args));
            }
        }

        return (byTarget, byLauncherAppId);
    }

    private List<string> FindShortcutsForLauncher(string appId, ShortcutTraversalCache cache)
    {
        return cache.FindWhere((target, args) =>
            target != null &&
            target.EndsWith(Constants.LauncherExeName, StringComparison.OrdinalIgnoreCase) &&
            args != null &&
            (args == appId || args.StartsWith(appId + " ")));
    }
}
