using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Apps.Shortcuts;

internal class ShortcutDiscoveryService(
    IShortcutTraversalScanner scanner)
    : IShortcutDiscoveryService
{
    public List<DiscoveredApp> DiscoverApps()
    {
        var seen = new Dictionary<string, DiscoveredApp>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in TraverseShortcuts())
        {
            if (entry.TargetPath != null &&
                Constants.DiscoverableExtensions.Contains(Path.GetExtension(entry.TargetPath)) &&
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
        => new(scanner.ScanShortcuts());

    public IEnumerable<ShortcutTraversalEntry> TraverseShortcuts()
        => scanner.ScanShortcuts();

    public List<string> FindShortcutsWhere(Func<string?, string?, bool> predicate)
    {
        return TraverseShortcuts()
            .Where(s => predicate(s.TargetPath, s.Arguments))
            .Select(s => s.Path)
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
