using Microsoft.Win32;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Apps.Shortcuts;

public class ShortcutDiscoveryService : IShortcutDiscoveryService
{
    public List<DiscoveredApp> DiscoverApps()
    {
        var seen = new Dictionary<string, DiscoveredApp>(StringComparer.OrdinalIgnoreCase);

        foreach (var (shortcutPath, target, _) in TraverseShortcuts())
        {
            if (target != null &&
                Constants.DiscoverableExtensions.Contains(Path.GetExtension(target)) &&
                !seen.ContainsKey(target) &&
                !ShortcutComHelper.IsUninstallShortcut(shortcutPath, target) &&
                !ShortcutComHelper.IsSystemExecutable(target))
            {
                var name = Path.GetFileNameWithoutExtension(shortcutPath);
                seen[target] = new DiscoveredApp(name, target);
            }
        }

        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        AddExesFromDirectory(seen, windowsDir, searchOption: SearchOption.TopDirectoryOnly);
        AddExesFromDirectory(seen, Path.Combine(windowsDir, "System32"), searchOption: SearchOption.TopDirectoryOnly);

        return seen.Values
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddExesFromDirectory(Dictionary<string, DiscoveredApp> seen, string dir, SearchOption searchOption)
    {
        if (!Directory.Exists(dir))
            return;
        string[] files;
        try
        {
            files = Directory.GetFiles(dir, "*.exe", searchOption);
        }
        catch
        {
            return;
        }

        foreach (var file in files)
        {
            if (!seen.ContainsKey(file) && HasEmbeddedIcon(file))
                seen[file] = new DiscoveredApp(Path.GetFileNameWithoutExtension(file), file);
        }
    }

    /// <summary>
    /// Returns true if the executable has at least one embedded icon resource.
    /// Used to filter out internal system tools (e.g. sfc.exe) that lack icons
    /// and are not meant for direct user launching.
    /// </summary>
    private static bool HasEmbeddedIcon(string exePath)
    {
        try
        {
            return ShortcutDiscoveryNative.ExtractIconEx(exePath, -1, null, null, 0) > 0;
        }
        catch
        {
            return false;
        }
    }

    public IEnumerable<(string path, string? target, string? args)> TraverseShortcuts()
    {
        var searchDirs = GetShortcutSearchDirectories();

        foreach (var dir in searchDirs)
        {
            if (!Directory.Exists(dir))
                continue;

            string[] files;
            try
            {
                files = Directory.GetFiles(dir, "*.lnk", SearchOption.AllDirectories);
            }
            catch
            {
                continue;
            } // skip inaccessible directories

            foreach (var lnk in files)
            {
                string? target, args;
                try
                {
                    (target, args) = ShortcutComHelper.GetShortcutTargetAndArgs(lnk);
                }
                catch
                {
                    continue;
                } // skip unreadable shortcuts

                yield return (lnk, target, args);
            }
        }
    }

    public List<string> FindShortcutsWhere(Func<string?, string?, bool> predicate)
    {
        return TraverseShortcuts()
            .Where(s => predicate(s.target, s.args))
            .Select(s => s.path)
            .ToList();
    }

    private static List<string> GetShortcutSearchDirectories()
    {
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Public locations
        var publicDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        var publicStartMenu = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
        if (!string.IsNullOrEmpty(publicDesktop))
            dirs.Add(publicDesktop);
        if (!string.IsNullOrEmpty(publicStartMenu))
            dirs.Add(publicStartMenu);

        // Per-user profiles from registry
        try
        {
            using var profileListKey = Registry.LocalMachine.OpenSubKey(
                Constants.ProfileListRegistryKey);

            if (profileListKey != null)
            {
                foreach (var sidStr in profileListKey.GetSubKeyNames())
                {
                    using var profileKey = profileListKey.OpenSubKey(sidStr);
                    if (profileKey?.GetValue("ProfileImagePath") is not string profilePath)
                        continue;

                    // Desktop: try relocated path from loaded user hive first
                    var desktop = TryGetUserShellFolder(sidStr, "Desktop", profilePath)
                                  ?? Path.Combine(profilePath, "Desktop");
                    dirs.Add(desktop);

                    var startMenu = Path.Combine(profilePath, "AppData", "Roaming",
                        "Microsoft", "Windows", "Start Menu");
                    dirs.Add(startMenu);

                    var taskBar = Path.Combine(profilePath, "AppData", "Roaming",
                        "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar");
                    dirs.Add(taskBar);
                }
            }
        }
        catch
        {
        } // best-effort; skip inaccessible registry / environment paths

        return dirs.Where(Directory.Exists).ToList();
    }

    private static string? TryGetUserShellFolder(string sid, string folderName, string profilePath)
    {
        try
        {
            using var userKey = Registry.Users.OpenSubKey(
                $@"{sid}\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders");

            var raw = userKey?.GetValue(folderName) as string;
            if (string.IsNullOrEmpty(raw))
                return null;

            // Manual expansion: replace %USERPROFILE% with the actual profile path
            // because Environment.ExpandEnvironmentVariables would use OUR profile
            var expanded = raw.Replace("%USERPROFILE%", profilePath, StringComparison.OrdinalIgnoreCase);

            // Expand any remaining standard env vars (e.g., %SystemDrive%)
            expanded = Environment.ExpandEnvironmentVariables(expanded);

            return expanded;
        }
        catch
        {
            // Hive not loaded or access denied — expected for non-logged-in users
            return null;
        }
    }
}