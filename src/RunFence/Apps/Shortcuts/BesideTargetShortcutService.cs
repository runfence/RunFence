using System.Text;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Apps.Shortcuts;

public class BesideTargetShortcutService(ILoggingService log, IShortcutProtectionService protection) : IBesideTargetShortcutService
{
    public void CreateBesideTargetShortcut(AppEntry app, string launcherPath, string iconPath, string username)
    {
        if (app.IsUrlScheme)
            return;

        var shortcutName = GetBesideTargetShortcutName(app, username);
        var paths = GetBesideTargetShortcutPaths(app, shortcutName);

        foreach (var shortcutPath in paths)
        {
            try
            {
                var dir = Path.GetDirectoryName(shortcutPath);
                if (dir != null && !Directory.Exists(dir))
                    continue;

                // Remove existing if present
                if (File.Exists(shortcutPath))
                {
                    protection.UnprotectShortcut(shortcutPath);
                    File.Delete(shortcutPath);
                }

                ShortcutComHelper.WithShortcut(shortcutPath, sc =>
                {
                    sc.TargetPath = launcherPath;
                    sc.Arguments = app.Id;
                    sc.WorkingDirectory = Path.GetDirectoryName(launcherPath) ?? "";
                    if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                        sc.IconLocation = $"{iconPath},0";
                    sc.Save();
                });

                var isInternal = app.IsFolder && IsInsideFolder(shortcutPath, app.ExePath);
                if (isInternal && !string.IsNullOrEmpty(app.AccountSid))
                    protection.ProtectInternalShortcut(shortcutPath, app.AccountSid);
                else
                    protection.ProtectShortcut(shortcutPath);

                log.Info($"Created beside-target shortcut: {shortcutPath}");
            }
            catch (Exception ex)
            {
                log.Error($"Failed to create beside-target shortcut: {shortcutPath}", ex);
                // Clean up orphaned .lnk on failure
                try
                {
                    if (File.Exists(shortcutPath))
                        File.Delete(shortcutPath);
                }
                catch
                {
                } // best-effort cleanup
            }
        }
    }

    public void RemoveBesideTargetShortcut(AppEntry app)
    {
        if (app.IsUrlScheme)
            return;

        var baseName = GetTargetBaseName(app);
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
                        var matches = ShortcutComHelper.WithShortcut(lnk, sc =>
                        {
                            string? target = sc.TargetPath;
                            string? args = sc.Arguments;
                            return target != null &&
                                   target.EndsWith(Constants.LauncherExeName, StringComparison.OrdinalIgnoreCase) &&
                                   args != null &&
                                   (args == app.Id || args.StartsWith(app.Id + " "));
                        });

                        if (matches)
                        {
                            protection.UnprotectShortcut(lnk);
                            File.Delete(lnk);
                            log.Info($"Removed beside-target shortcut: {lnk}");
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Error($"Failed to remove beside-target shortcut: {lnk}", ex);
                    }
                }
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
                        protection.ProtectShortcut(shortcutPath);
                }
            }
            catch (Exception ex)
            {
                log.Error($"Failed to enforce beside-target shortcuts for {app.Name}", ex);
            }
        }
    }

    public static string GetBesideTargetShortcutName(AppEntry app, string username)
    {
        var baseName = GetTargetBaseName(app);
        var sanitizedUsername = SanitizeFilename(username);
        return $"{baseName}-as-{sanitizedUsername}.lnk";
    }

    public static List<string> GetBesideTargetShortcutPaths(AppEntry app, string shortcutName)
    {
        var dirs = GetBesideTargetDirectories(app);
        return dirs.Select(dir => Path.Combine(dir, shortcutName)).ToList();
    }

    private static List<string> GetBesideTargetDirectories(AppEntry app)
    {
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

    private static string SanitizeFilename(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            sb.Append(Array.IndexOf(invalidChars, c) < 0 ? c : '_');
        }

        return sb.ToString();
    }

    private static bool IsBesideTargetShortcutUpToDate(string shortcutPath, AppEntry app, string launcherPath, string iconPath)
    {
        return ShortcutComHelper.WithShortcut(shortcutPath, sc =>
        {
            string? target = sc.TargetPath;
            string? args = sc.Arguments;
            string? icon = sc.IconLocation;

            if (!string.Equals(target, launcherPath, StringComparison.OrdinalIgnoreCase))
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
}