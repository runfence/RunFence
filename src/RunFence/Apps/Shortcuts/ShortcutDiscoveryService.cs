using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Apps.Shortcuts;

internal class ShortcutDiscoveryService(
    IShortcutTraversalScanner scanner,
    IDatabaseProvider databaseProvider)
    : IShortcutDiscoveryService
{
    public List<DiscoveredApp> DiscoverApps()
    {
        var seen = new Dictionary<string, DiscoveredApp>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in TraverseShortcuts())
        {
            if (entry.TargetPath != null &&
                PathConstants.DiscoverableExtensions.Contains(Path.GetExtension(entry.TargetPath)) &&
                !seen.ContainsKey(entry.TargetPath) &&
                !ShortcutClassificationHelper.IsUninstallShortcut(entry.Path, entry.TargetPath) &&
                !ShortcutClassificationHelper.IsSystemExecutable(entry.TargetPath))
            {
                var name = Path.GetFileNameWithoutExtension(entry.Path);
                seen[entry.TargetPath] = new DiscoveredApp(name, entry.TargetPath);
            }
        }

        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        AddExesFromDirectory(seen, windowsDir, searchOption: SearchOption.TopDirectoryOnly);
        AddExesFromDirectory(seen, Path.Combine(windowsDir, "System32"), searchOption: SearchOption.TopDirectoryOnly);

        return seen.Values
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public ShortcutTraversalCache CreateTraversalCache()
        => new(scanner.ScanShortcuts(CaptureManagedSids()));

    public ShortcutTraversalCache CreateTraversalCache(HashSet<string>? managedSids)
        => new(scanner.ScanShortcuts(managedSids));

    public ShortcutTraversalCache CreateTraversalCacheIfNeeded(IEnumerable<AppEntry> apps)
        => apps.Any(a => a.ManageShortcuts)
            ? CreateTraversalCache()
            : new ShortcutTraversalCache([]);

    public HashSet<string>? CaptureManagedSids()
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

    private IEnumerable<ShortcutTraversalEntry> TraverseShortcuts()
        => scanner.ScanShortcuts(CaptureManagedSids());

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
}
