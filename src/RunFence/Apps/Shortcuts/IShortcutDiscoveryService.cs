using RunFence.Core.Models;

namespace RunFence.Apps.Shortcuts;

public interface IShortcutDiscoveryService
{
    List<DiscoveredApp> DiscoverApps();
    IEnumerable<(string path, string? target, string? args)> TraverseShortcuts();
    List<string> FindShortcutsWhere(Func<string?, string?, bool> predicate);
}