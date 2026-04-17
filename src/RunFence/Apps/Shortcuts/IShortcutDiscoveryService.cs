using RunFence.Core.Models;

namespace RunFence.Apps.Shortcuts;

public interface IShortcutDiscoveryService
{
    List<DiscoveredApp> DiscoverApps();
    ShortcutTraversalCache CreateTraversalCache();
    IEnumerable<ShortcutTraversalEntry> TraverseShortcuts();
    List<string> FindShortcutsWhere(Func<string?, string?, bool> predicate);
}
