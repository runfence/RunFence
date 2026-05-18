using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launching.Resolution;

namespace RunFence.Apps.Shortcuts;

internal class BesideTargetShortcutService(
    ILoggingService log,
    IShortcutProtectionService protection,
    IShortcutComHelper shortcutHelper,
    IShortcutWriteAccessService shortcutWriteAccessService,
    IShortcutFilePersistenceNative shortcutFilePersistenceNative,
    IExecutableKindService executableKindService)
    : IBesideTargetShortcutService
{
    public void CreateBesideTargetShortcut(AppEntry app, string launcherPath, string iconPath, string username)
    {
        if (app.IsUrlScheme)
            return;
        if (executableKindService.IsUwpExeFile(app.ExePath))
            return;

        // Managed suffix naming is used for discovery/reconciliation and is not a trust boundary for ownership.
        var shortcutName = GetBesideTargetShortcutName(app, username);
        var paths = GetBesideTargetShortcutPaths(app, shortcutName);

        foreach (var shortcutPath in paths)
        {
            try
            {
                var dir = Path.GetDirectoryName(shortcutPath);
                if (dir != null && !Directory.Exists(dir))
                    continue;

                shortcutWriteAccessService.Save(shortcutPath, new ShortcutMutation(
                    launcherPath,
                    app.Id,
                    ShortcutPathHelper.GetLauncherWorkingDirectory(launcherPath),
                    !string.IsNullOrEmpty(iconPath) && File.Exists(iconPath) ? $"{iconPath},0" : null,
                    !string.IsNullOrEmpty(iconPath) && File.Exists(iconPath) ? ShortcutIconUpdateMode.Set : ShortcutIconUpdateMode.None,
                    null,
                    null,
                    1),
                    ShortcutDestinationMetadataMode.PreserveExisting,
                    ShortcutContentMode.RecreateCanonical);

                var isInternal = app.IsFolder && IsInsideFolder(shortcutPath, app.ExePath);
                if (isInternal && !string.IsNullOrEmpty(app.AccountSid))
                    protection.ProtectInternalShortcut(shortcutPath, app.AccountSid);
                else
                    protection.ProtectShortcut(shortcutPath, allowAdministratorsDelete: true);

                log.Info($"Created beside-target shortcut: {shortcutPath}");
            }
            catch (ShortcutProtectionException)
            {
                TryDeleteFailedShortcut(shortcutPath);

                throw;
            }
            catch (Exception ex)
            {
                log.Error($"Failed to create beside-target shortcut: {shortcutPath}", ex);
                // Clean up orphaned .lnk on failure
                TryDeleteFailedShortcut(shortcutPath);
            }
        }
    }

    public void RemoveBesideTargetShortcut(AppEntry app)
    {
        if (app.IsUrlScheme)
            return;

        var baseName = GetTargetBaseName(app);
        // Managed beside-target shortcuts are intentionally located by suffix pattern (baseName-as-<user>.lnk);
        // this is for RunFence-managed matching only, not for generic shortcut ownership proof.
        var searchPattern = $"{baseName}-as-*.lnk";
        var directories = GetBesideTargetDirectories(app);

        foreach (var dir in directories)
        {
            if (!Directory.Exists(dir))
                continue;

            try
            {
                foreach (var lnk in Directory.GetFiles(dir, searchPattern))
                {
                    try
                    {
                        var matches = shortcutHelper.WithShortcut(lnk, sc =>
                        {
                            string? target = sc.TargetPath;
                            string? args = sc.Arguments;
                            return target != null &&
                                   target.EndsWith(PathConstants.LauncherExeName, StringComparison.OrdinalIgnoreCase) &&
                                   string.Equals(
                                       ShortcutClassificationHelper.TryGetManagedShortcutAppId(args),
                                       app.Id,
                                       StringComparison.Ordinal);
                        });

                        if (matches)
                        {
                            shortcutFilePersistenceNative.DeleteExistingDestination(lnk);
                            log.Info($"Removed beside-target shortcut: {lnk}");
                        }
                    }
                    catch (ShortcutProtectionException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        log.Error($"Failed to remove beside-target shortcut: {lnk}", ex);
                    }
                }
            }
            catch (ShortcutProtectionException)
            {
                throw;
            }
            catch (Exception ex)
            {
                log.Error($"Failed to scan directory for beside-target shortcuts: {dir}", ex);
            }
        }
    }

    public void EnforceBesideTargetShortcuts(IEnumerable<AppEntry> apps, string launcherPath,
        Func<AppEntry, (string username, string iconPath)?> resolveAppInfo)
    {
        foreach (var app in apps)
        {
            if (app.IsUrlScheme)
                continue;

            try
            {
                var info = resolveAppInfo(app);
                if (info == null)
                    continue;

                var (username, iconPath) = info.Value;
                // Enforcement intentionally keeps suffix-based managed matching scoped to discovery/reconciliation.
                var shortcutName = GetBesideTargetShortcutName(app, username);
                var paths = GetBesideTargetShortcutPaths(app, shortcutName);

                foreach (var shortcutPath in paths)
                {
                    if (!File.Exists(shortcutPath) || !IsBesideTargetShortcutUpToDate(shortcutPath, app, launcherPath, iconPath))
                    {
                        CreateBesideTargetShortcut(app, launcherPath, iconPath, username);
                        break; // CreateBesideTargetShortcut handles all paths
                    }

                    // Content is correct — ensure protection is in place (skips write if already protected)
                    var isInternal = app.IsFolder && IsInsideFolder(shortcutPath, app.ExePath);
                    if (isInternal && !string.IsNullOrEmpty(app.AccountSid))
                        protection.ProtectInternalShortcut(shortcutPath, app.AccountSid);
                    else
                        protection.ProtectShortcut(shortcutPath, allowAdministratorsDelete: true);
                }
            }
            catch (ShortcutProtectionException)
            {
                throw;
            }
            catch (Exception ex)
            {
                log.Error($"Failed to enforce beside-target shortcuts for {app.Name}", ex);
            }
        }
    }

    internal static string GetBesideTargetShortcutName(AppEntry app, string username)
    {
        var baseName = GetTargetBaseName(app);
        // Suffix-based managed naming preserves deterministic discovery under multiple accounts.
        var sanitizedUsername = ShortcutPathHelper.SanitizeFileName(username);
        return $"{baseName}-as-{sanitizedUsername}.lnk";
    }

    internal static List<string> GetBesideTargetShortcutPaths(AppEntry app, string shortcutName)
    {
        var dirs = GetBesideTargetDirectories(app);
        return dirs.Select(dir => Path.Combine(dir, shortcutName)).ToList();
    }

    private static bool IsWindowsPath(string path)
    {
        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(windowsDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || fullPath.Equals(windowsDir, StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> GetBesideTargetDirectories(AppEntry app)
    {
        if (IsWindowsPath(app.ExePath))
            return [];

        var dirs = new List<string>();

        if (app.IsFolder)
        {
            var fullPath = Path.GetFullPath(app.ExePath);
            var parentDir = Path.GetDirectoryName(fullPath);
            if (parentDir != null)
                dirs.Add(parentDir);
            dirs.Add(fullPath);
        }
        else
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(app.ExePath));
            if (dir != null)
                dirs.Add(dir);
        }

        return dirs;
    }

    private static string GetTargetBaseName(AppEntry app)
    {
        if (app.IsFolder)
            return Path.GetFileName(Path.GetFullPath(app.ExePath).TrimEnd(Path.DirectorySeparatorChar));
        return Path.GetFileNameWithoutExtension(app.ExePath);
    }

    private bool IsBesideTargetShortcutUpToDate(string shortcutPath, AppEntry app, string launcherPath, string iconPath)
    {
        return shortcutHelper.WithShortcut(shortcutPath, sc =>
        {
            // Recheck logic intentionally uses launcher + argument fields only for managed-suffix shortcut freshness,
            // and is not used as complete ownership verification.
            string? target = sc.TargetPath;
            string? args = sc.Arguments;
            string? workingDirectory = sc.WorkingDirectory;
            string? icon = sc.IconLocation;

            if (target == null || !PathHelper.IsSamePath(target, launcherPath))
                return false;
            string right = ShortcutPathHelper.GetLauncherWorkingDirectory(launcherPath);
            if (!PathHelper.IsSamePath(workingDirectory ?? "", right))
                return false;
            if (args != app.Id)
                return false;
            // Only require the icon if the icon file exists; it may not be generated yet
            if (File.Exists(iconPath) &&
                !string.Equals(icon, $"{iconPath},0", StringComparison.OrdinalIgnoreCase))
                return false;
            return true;
        });
    }

    private static bool IsInsideFolder(string shortcutPath, string folderPath)
    {
        var normalizedShortcut = Path.GetFullPath(shortcutPath);
        var normalizedFolder = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar)
                               + Path.DirectorySeparatorChar;
        return normalizedShortcut.StartsWith(normalizedFolder, StringComparison.OrdinalIgnoreCase);
    }

    private void TryDeleteFailedShortcut(string shortcutPath)
    {
        try
        {
            if (!File.Exists(shortcutPath))
                return;
            shortcutFilePersistenceNative.DeleteExistingDestination(shortcutPath);
        }
        catch
        {
        }
    }

}
