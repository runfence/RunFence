using RunFence.Core.Models;

namespace RunFence.Apps.Shortcuts;

public interface IShortcutDiscoveryService
{
    List<DiscoveredApp> DiscoverApps();
    /// <summary>
    /// Captures the managed SID set from the live database on the calling thread, then builds the
    /// traversal cache. Must be called on the UI thread — use <see cref="CreateTraversalCache(HashSet{string}?)"/>
    /// for background-thread use with a pre-captured SID set.
    /// </summary>
    ShortcutTraversalCache CreateTraversalCache();
    /// <summary>
    /// Builds the traversal cache using the pre-captured <paramref name="managedSids"/> set.
    /// Safe to call from a background thread — does not access the live database.
    /// </summary>
    ShortcutTraversalCache CreateTraversalCache(HashSet<string>? managedSids);
    /// <summary>
    /// Captures the set of SIDs for all managed accounts and apps from the live database.
    /// Must be called on the UI thread. Returns null on failure (treated as no SID filtering by the scanner).
    /// </summary>
    HashSet<string>? CaptureManagedSids();
    /// <summary>
    /// Returns a full traversal cache if any app in <paramref name="apps"/> has
    /// <see cref="AppEntry.ManageShortcuts"/> set; otherwise returns an empty cache without scanning.
    /// Has the same threading requirements as <see cref="CreateTraversalCache()"/> when scanning is needed.
    /// </summary>
    ShortcutTraversalCache CreateTraversalCacheIfNeeded(IEnumerable<AppEntry> apps);
}
