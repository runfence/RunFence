using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Apps.Shortcuts;

public class ShortcutService : IShortcutService
{
    private readonly ILoggingService _log;
    private readonly IShortcutProtectionService _protection;
    private readonly IBesideTargetShortcutService _besideTarget;
    private readonly IShortcutDiscoveryService _discovery;

    public ShortcutService(
        ILoggingService log,
        IShortcutProtectionService protection,
        IBesideTargetShortcutService besideTarget,
        IShortcutDiscoveryService discovery)
    {
        _log = log;
        _protection = protection;
        _besideTarget = besideTarget;
        _discovery = discovery;
    }

    public void ReplaceShortcuts(AppEntry app, string launcherPath, string iconPath)
    {
        var shortcuts = app.IsFolder
            ? FindShortcutsForFolder(app.ExePath)
            : FindShortcutsForExe(app.ExePath);

        foreach (var shortcutPath in shortcuts)
        {
            try
            {
                ReplaceShortcut(shortcutPath, app, launcherPath, iconPath);
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to replace shortcut: {shortcutPath}", ex);
            }
        }
    }

    public void SaveShortcut(AppEntry app, string shortcutPath)
    {
        var launcherPath = Path.Combine(AppContext.BaseDirectory, Constants.LauncherExeName);
        var iconPath = Path.Combine(Constants.ProgramDataIconDir, $"{app.Id}.ico");
        ShortcutComHelper.WithShortcut(shortcutPath, sc =>
        {
            sc.TargetPath = launcherPath;
            sc.Arguments = app.Id;
            sc.WorkingDirectory = AppContext.BaseDirectory;
            if (File.Exists(iconPath))
                sc.IconLocation = $"{iconPath},0";
            sc.Save();
        });
    }

    public void RevertShortcuts(AppEntry app)
    {
        var shortcuts = FindShortcutsForLauncher(app.Id);

        foreach (var shortcutPath in shortcuts)
        {
            try
            {
                RevertSingleShortcut(shortcutPath, app);
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to revert shortcut: {shortcutPath}", ex);
            }
        }
    }

    public void UpdateShortcutToLauncher(string shortcutPath, string appId, string launcherPath, string? iconPath)
    {
        _protection.UnprotectShortcut(shortcutPath);

        ShortcutComHelper.WithShortcut(shortcutPath, sc =>
        {
            string currentArgs = sc.Arguments ?? "";

            sc.TargetPath = launcherPath;
            sc.Arguments = string.IsNullOrEmpty(currentArgs)
                ? appId
                : $"{appId} {currentArgs}";

            if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                sc.IconLocation = $"{iconPath},0";

            sc.Save();
        });

        _protection.ProtectShortcut(shortcutPath);
        _log.Info($"Updated shortcut to launcher: {shortcutPath}");
    }

    public bool RevertSingleShortcut(string shortcutPath, AppEntry app)
    {
        // Single COM open to check args and, if valid, write the reverted shortcut.
        string? parsedArgs = null;
        string? currentArgsForWarning = null;

        var canRevert = ShortcutComHelper.WithShortcut(shortcutPath, sc =>
        {
            string currentArgs = sc.Arguments ?? "";
            parsedArgs = ShortcutComHelper.ParseManagedShortcutArgs(currentArgs, app.Id);
            if (parsedArgs == null)
                currentArgsForWarning = currentArgs;
            return parsedArgs != null;
        });

        if (!canRevert)
        {
            _log.Warn($"Shortcut {shortcutPath} has unexpected args, cannot revert: {currentArgsForWarning}");
            return false;
        }

        _protection.UnprotectShortcut(shortcutPath);

        ShortcutComHelper.WithShortcut(shortcutPath, sc =>
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

        _log.Info($"Reverted shortcut: {shortcutPath}");
        return true;
    }

    public void EnforceShortcuts(IEnumerable<AppEntry> apps, string launcherPath)
    {
        var appList = apps.Where(a => a.ManageShortcuts).ToList();
        if (appList.Count == 0)
            return;

        // Single scan — O(totalShortcuts) COM calls instead of O(N * totalShortcuts)
        var (byTarget, byLauncherAppId) = ScanAllShortcuts();

        foreach (var app in appList)
        {
            byTarget.TryGetValue(app.ExePath, out var shortcuts);
            var shortcutList = shortcuts != null ? new List<string>(shortcuts) : new List<string>();

            // For folder apps, find shortcuts via the shared folder matching logic
            if (app.IsFolder)
            {
                var normalizedFolder = Path.GetFullPath(app.ExePath).TrimEnd(Path.DirectorySeparatorChar);
                foreach (var (target, targetShortcuts) in byTarget)
                {
                    foreach (var lnk in targetShortcuts)
                    {
                        if (shortcutList.Contains(lnk, StringComparer.OrdinalIgnoreCase))
                            continue;

                        // For explorer.exe targets, need to check args via COM
                        string? args = null;
                        if (target.EndsWith("explorer.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                args = ShortcutComHelper.WithShortcut(lnk, sc => (string?)sc.Arguments);
                            }
                            catch
                            {
                                continue;
                            }
                        }

                        if (ShortcutComHelper.IsFolderShortcutTarget(target, args, normalizedFolder))
                            shortcutList.Add(lnk);
                    }
                }
            }

            byLauncherAppId.TryGetValue(app.Id, out var existingManaged);
            var managedList = existingManaged ?? new List<string>();

            bool hasNewShortcuts = shortcutList.Any(s =>
                !managedList.Contains(s, StringComparer.OrdinalIgnoreCase));

            if (hasNewShortcuts)
            {
                try
                {
                    var iconPath = Path.Combine(Constants.ProgramDataIconDir, $"{app.Id}.ico");
                    ReplaceShortcutsFromList(app, launcherPath, iconPath, shortcutList);
                }
                catch (Exception ex)
                {
                    _log.Error($"Failed to enforce shortcut for {app.Name}", ex);
                }
            }

            foreach (var shortcutPath in managedList)
            {
                if (File.Exists(shortcutPath))
                    _protection.ProtectShortcut(shortcutPath);
            }
        }
    }

    public List<DiscoveredApp> DiscoverApps() => _discovery.DiscoverApps();

    // --- IShortcutService beside-target delegates ---

    public void CreateBesideTargetShortcut(AppEntry app, string launcherPath, string iconPath, string username)
        => _besideTarget.CreateBesideTargetShortcut(app, launcherPath, iconPath, username);

    public void RemoveBesideTargetShortcut(AppEntry app)
        => _besideTarget.RemoveBesideTargetShortcut(app);

    public void EnforceBesideTargetShortcuts(IEnumerable<AppEntry> apps, string launcherPath,
        Func<AppEntry, (string username, string iconPath)?> resolveAppInfo)
        => _besideTarget.EnforceBesideTargetShortcuts(apps, launcherPath, resolveAppInfo);

    // --- Shortcut search ---

    private List<string> FindShortcutsForExe(string exePath)
    {
        var normalized = Path.GetFullPath(exePath);
        return _discovery.FindShortcutsWhere((target, _) =>
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

    private List<string> FindShortcutsForFolder(string folderPath)
    {
        var normalizedFolder = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar);
        return _discovery.FindShortcutsWhere((target, args) =>
            ShortcutComHelper.IsFolderShortcutTarget(target, args, normalizedFolder));
    }

    private void ReplaceShortcut(string shortcutPath, AppEntry app, string launcherPath, string iconPath)
        => UpdateShortcutToLauncher(shortcutPath, app.Id, launcherPath, iconPath);

    private void ReplaceShortcutsFromList(AppEntry app, string launcherPath, string iconPath, List<string> shortcuts)
    {
        foreach (var shortcutPath in shortcuts)
        {
            try
            {
                ReplaceShortcut(shortcutPath, app, launcherPath, iconPath);
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to replace shortcut: {shortcutPath}", ex);
            }
        }
    }

    private (Dictionary<string, List<string>> byTarget, Dictionary<string, List<string>> byLauncherAppId) ScanAllShortcuts()
    {
        var byTarget = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var byLauncherAppId = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (lnk, target, args) in _discovery.TraverseShortcuts())
        {
            if (target == null)
                continue;

            // Index by target path
            if (!byTarget.TryGetValue(target, out var targetList))
            {
                targetList = new List<string>();
                byTarget[target] = targetList;
            }

            targetList.Add(lnk);

            // Index launcher-managed shortcuts by app ID
            if (target.EndsWith(Constants.LauncherExeName, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(args))
            {
                var spaceIndex = args.IndexOf(' ');
                var appId = spaceIndex < 0 ? args : args[..spaceIndex];

                if (!byLauncherAppId.TryGetValue(appId, out var idList))
                {
                    idList = new List<string>();
                    byLauncherAppId[appId] = idList;
                }

                idList.Add(lnk);
            }
        }

        return (byTarget, byLauncherAppId);
    }

    private List<string> FindShortcutsForLauncher(string appId)
    {
        return _discovery.FindShortcutsWhere((target, args) =>
            target != null &&
            target.EndsWith(Constants.LauncherExeName, StringComparison.OrdinalIgnoreCase) &&
            args != null &&
            (args == appId || args.StartsWith(appId + " ")));
    }

    public IEnumerable<(string path, string? target, string? args)> TraverseShortcuts()
        => _discovery.TraverseShortcuts();

    public List<string> FindShortcutsWhere(Func<string?, string?, bool> predicate)
        => _discovery.FindShortcutsWhere(predicate);
}