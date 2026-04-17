using Microsoft.Win32;
using RunFence.Core;
using RunFence.Persistence;

namespace RunFence.Apps.Shortcuts;

internal sealed class ShortcutTraversalScanner(IShortcutComHelper shortcutHelper, IDatabaseProvider databaseProvider) : IShortcutTraversalScanner
{
    public IEnumerable<ShortcutTraversalEntry> ScanShortcuts()
    {
        HashSet<string>? managedSids = TryGetManagedSids();
        var searchDirs = GetShortcutSearchDirectories(managedSids);

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
            }

            foreach (var lnk in files)
            {
                string? target;
                string? args;
                try
                {
                    (target, args) = shortcutHelper.GetShortcutTargetAndArgs(lnk);
                }
                catch
                {
                    continue;
                }

                yield return new ShortcutTraversalEntry(lnk, target, args);
            }
        }
    }

    private HashSet<string>? TryGetManagedSids()
    {
        try
        {
            var database = databaseProvider.GetDatabase();
            var sids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var account in database.Accounts)
                if (!string.IsNullOrEmpty(account.Sid))
                    sids.Add(account.Sid);
            foreach (var app in database.Apps)
                if (!string.IsNullOrEmpty(app.AccountSid))
                    sids.Add(app.AccountSid);

            // Include the interactive user's SID so their profile shortcuts are discovered
            // even when no account entry has been created for them yet.
            var interactiveSid = SidResolutionHelper.GetInteractiveUserSid();
            if (interactiveSid != null)
                sids.Add(interactiveSid);

            return sids;
        }
        catch
        {
            return null;
        }
    }

    private static List<string> GetShortcutSearchDirectories(HashSet<string>? managedSids)
    {
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var publicDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        var publicStartMenu = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
        if (!string.IsNullOrEmpty(publicDesktop))
            dirs.Add(publicDesktop);
        if (!string.IsNullOrEmpty(publicStartMenu))
            dirs.Add(publicStartMenu);

        try
        {
            using var profileListKey = Registry.LocalMachine.OpenSubKey(Constants.ProfileListRegistryKey);

            if (profileListKey != null)
            {
                foreach (var sidStr in profileListKey.GetSubKeyNames())
                {
                    // Skip non-managed accounts when SID list is available
                    if (managedSids != null && !managedSids.Contains(sidStr))
                        continue;

                    using var profileKey = profileListKey.OpenSubKey(sidStr);
                    if (profileKey?.GetValue("ProfileImagePath") is not string profilePath)
                        continue;

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
        }

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

            var expanded = raw.Replace("%USERPROFILE%", profilePath, StringComparison.OrdinalIgnoreCase);
            return Environment.ExpandEnvironmentVariables(expanded);
        }
        catch
        {
            return null;
        }
    }
}
