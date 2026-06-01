using RunFence.Acl.Traverse;
using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Acl;

public class GrantIntentStoreMutationService(
    TraverseGrantStateService traverseGrantStateService,
    Func<IGrantIntentStoreProvider> grantIntentStoreProvider,
    Func<IGrantIntentRepository> grantIntentRepository,
    Func<IGrantIntentStore> mainGrantIntentStore)
{
    public readonly record struct GrantIntentSnapshot(
        IGrantIntentStore Store,
        IReadOnlyList<GrantedPathEntry> Entries);

    private IGrantIntentStoreProvider GrantIntentStoreProvider => grantIntentStoreProvider();
    private IGrantIntentRepository GrantIntentRepository => grantIntentRepository();
    private IGrantIntentStore MainGrantIntentStore => mainGrantIntentStore();

    public List<GrantIntentLocation> GetGrantLocationsForPath(string sid, string normalizedPath)
        => GrantIntentRepository.FindEntriesForSid(sid)
            .Where(location =>
                !location.Entry.IsTraverseOnly &&
                string.Equals(location.Entry.Path, normalizedPath, StringComparison.OrdinalIgnoreCase))
            .ToList();

    public List<GrantIntentLocation> GetAllGrantLocations(string sid)
        => GrantIntentRepository.FindEntriesForSid(sid)
            .Where(location => !location.Entry.IsTraverseOnly)
            .ToList();

    public List<IGrantIntentStore> ResolveFinalStores(
        IGrantIntentStore? selectedStore,
        IReadOnlyList<GrantIntentLocation> sameModeLocations,
        IReadOnlyList<GrantIntentLocation> oppositeModeLocations,
        bool allowOppositeModeSwitch)
    {
        if (selectedStore != null)
            return [selectedStore];

        var existingLocations = allowOppositeModeSwitch && oppositeModeLocations.Count > 0
            ? oppositeModeLocations
            : sameModeLocations;
        if (existingLocations.Count > 0)
            return existingLocations.Select(location => location.Store).Distinct().ToList();

        return [MainGrantIntentStore];
    }

    public bool WouldMutateGrantStores(
        GrantedPathEntry newEntry,
        IReadOnlyList<GrantIntentLocation> allLocations,
        IReadOnlyList<IGrantIntentStore> finalStores)
    {
        var finalStoreSet = finalStores.ToHashSet();

        foreach (var location in allLocations)
        {
            if (!finalStoreSet.Contains(location.Store) ||
                location.Entry.IsDeny != newEntry.IsDeny)
            {
                return true;
            }
        }

        foreach (var targetStore in finalStores)
        {
            var currentEntry = allLocations.FirstOrDefault(location =>
                ReferenceEquals(location.Store, targetStore) &&
                location.Entry.IsDeny == newEntry.IsDeny)?.Entry;
            if (currentEntry == null)
                return true;

            if (!traverseGrantStateService.EntriesEquivalent(currentEntry, newEntry))
                return true;
        }

        return false;
    }

    public void MutateGrantStores(
        string sid,
        GrantedPathEntry newEntry,
        IReadOnlyList<GrantIntentLocation> allLocations,
        IReadOnlyList<IGrantIntentStore> finalStores)
    {
        var finalStoreSet = finalStores.ToHashSet();

        foreach (var location in allLocations)
        {
            if (!finalStoreSet.Contains(location.Store) ||
                location.Entry.IsDeny != newEntry.IsDeny)
            {
                location.Store.RemoveEntry(sid, location.Entry);
            }
        }

        foreach (var targetStore in finalStores)
        {
            var currentEntry = allLocations.FirstOrDefault(location =>
                ReferenceEquals(location.Store, targetStore) &&
                location.Entry.IsDeny == newEntry.IsDeny)?.Entry;
            if (currentEntry == null)
            {
                targetStore.AddEntry(sid, newEntry);
                continue;
            }

            if (!traverseGrantStateService.EntriesEquivalent(currentEntry, newEntry))
                targetStore.ReplaceEntry(sid, currentEntry, newEntry);
        }
    }

    public bool RestoreGrantStoresToExactLocations(
        string sid,
        IReadOnlyList<GrantIntentLocation> currentLocations,
        IReadOnlyList<GrantIntentRestoreLocation> desiredLocations,
        bool mutate)
    {
        var desiredByConfigPath = desiredLocations
            .GroupBy(location => NormalizeConfigPath(location.StoreIdentity.ConfigPath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        bool modified = false;

        foreach (var location in currentLocations)
        {
            var configPath = NormalizeConfigPath(location.Store.ConfigPath);
            if (!desiredByConfigPath.ContainsKey(configPath))
            {
                modified = true;
                if (mutate)
                    location.Store.RemoveEntry(sid, location.Entry);
            }
        }

        foreach (var desired in desiredByConfigPath.Values)
        {
            var currentEntry = currentLocations.FirstOrDefault(location =>
                string.Equals(
                    NormalizeConfigPath(location.Store.ConfigPath),
                    NormalizeConfigPath(desired.StoreIdentity.ConfigPath),
                    StringComparison.OrdinalIgnoreCase))?.Entry;
            if (currentEntry == null)
            {
                modified = true;
                if (mutate)
                    GrantIntentStoreProvider.ResolveStore(desired.StoreIdentity.ConfigPath).AddEntry(sid, desired.Entry);
                continue;
            }

            if (!traverseGrantStateService.EntriesEquivalent(currentEntry, desired.Entry))
            {
                modified = true;
                if (mutate)
                {
                    GrantIntentStoreProvider.ResolveStore(desired.StoreIdentity.ConfigPath)
                        .ReplaceEntry(sid, currentEntry, desired.Entry);
                }
            }
        }

        return modified;
    }

    public IReadOnlyList<GrantIntentSnapshot> CaptureGrantSnapshots(
        string sid,
        string normalizedPath,
        IEnumerable<IGrantIntentStore> stores)
        => stores
            .Distinct()
            .Select(store => new GrantIntentSnapshot(
                store,
                store.GetEntries(sid)
                    .Where(entry =>
                        !entry.IsTraverseOnly &&
                        string.Equals(entry.Path, normalizedPath, StringComparison.OrdinalIgnoreCase))
                    .Select(entry => entry.Clone())
                    .ToList()))
            .ToList();

    public void RestoreGrantSnapshots(
        string sid,
        string normalizedPath,
        IReadOnlyList<GrantIntentSnapshot> snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            var currentEntries = snapshot.Store.GetEntries(sid)
                .Where(entry =>
                    !entry.IsTraverseOnly &&
                    string.Equals(entry.Path, normalizedPath, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var entry in currentEntries)
                snapshot.Store.RemoveEntry(sid, entry);

            foreach (var entry in snapshot.Entries)
                snapshot.Store.AddEntry(sid, entry);
        }
    }

    public void RemoveGrantEntries(string sid, string normalizedPath, IEnumerable<GrantIntentLocation> locations)
    {
        foreach (var location in locations)
        {
            if (!string.Equals(location.Entry.Path, normalizedPath, StringComparison.OrdinalIgnoreCase))
                continue;

            location.Store.RemoveEntry(sid, location.Entry);
        }
    }

    private static string NormalizeConfigPath(string? configPath)
        => configPath == null ? string.Empty : Path.GetFullPath(configPath);
}
