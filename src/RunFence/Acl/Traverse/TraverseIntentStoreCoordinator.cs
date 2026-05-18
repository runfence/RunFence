using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Acl.Traverse;

public class TraverseIntentStoreCoordinator(
    Func<IGrantIntentRepository> grantIntentRepository,
    ITraverseGrantOwnerResolver ownerResolver) : ITraverseIntentStoreCoordinator
{
    private IGrantIntentRepository GrantIntentRepository => grantIntentRepository();

    public bool UsesSharedContainerTraverse(string sid)
        => ownerResolver.UsesSharedContainerTraverse(sid);

    public string ResolveStorageOwnerSid(string sid)
        => ownerResolver.ResolveStorageOwnerSid(sid);

    public string ResolveAclSid(string sid)
        => ownerResolver.ResolveAclSid(sid);

    public List<GrantedPathEntry> GetOrCreateTraverseStore(AppDatabase database, string sid)
        => ownerResolver.GetOrCreateTraverseStore(database, sid);

    public List<GrantedPathEntry> GetTraverseStoreOrEmpty(AppDatabase database, string sid)
        => ownerResolver.GetTraverseStoreOrEmpty(database, sid);

    public IEnumerable<AccountEntry> GetGrantOwnersForTraverseCleanup(AppDatabase database, string sid)
        => ownerResolver.GetGrantOwnersForTraverseCleanup(database, sid);

    public List<GrantIntentLocation> GetTraverseLocationsForPath(
        string sid,
        string normalizedPath,
        bool includeManualSharedEntries)
        => GrantIntentRepository.FindEntriesForSid(ownerResolver.ResolveStorageOwnerSid(sid))
            .Where(location =>
                location.Entry.IsTraverseOnly &&
                string.Equals(location.Entry.Path, normalizedPath, StringComparison.OrdinalIgnoreCase) &&
                ownerResolver.EntryAppliesToSid(location.Entry, sid, includeManualSharedEntries))
            .ToList();

    public List<GrantIntentLocation> GetAllTraverseLocations(string sid)
        => GrantIntentRepository.FindEntriesForSid(ownerResolver.ResolveStorageOwnerSid(sid))
            .Where(location =>
                location.Entry.IsTraverseOnly &&
                ownerResolver.EntryAppliesToSid(location.Entry, sid, includeManualSharedEntries: false))
            .ToList();

    public GrantedPathEntry BuildTraverseEntry(
        string sid,
        string normalizedPath,
        IReadOnlyList<string> coveragePaths,
        GrantedPathEntry? currentEntry)
    {
        var entry = currentEntry?.Clone() ?? new GrantedPathEntry();
        entry.Path = normalizedPath;
        entry.IsTraverseOnly = true;
        entry.IsDeny = false;
        entry.SavedRights = null;
        entry.PreviousSaclLabel = null;
        entry.OwnerContainerSid = null;
        entry.AllAppliedPaths = coveragePaths.ToList();

        if (!ownerResolver.UsesSharedContainerTraverse(sid))
            return entry;

        if (currentEntry == null)
        {
            entry.SourceSids = [sid];
            return entry;
        }

        if (currentEntry.SourceSids == null)
        {
            entry.SourceSids = null;
            return entry;
        }

        entry.SourceSids = currentEntry.SourceSids
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!entry.SourceSids.Contains(sid, StringComparer.OrdinalIgnoreCase))
            entry.SourceSids.Add(sid);
        return entry;
    }

    public bool RemoveTraverseEntryFromStore(string sid, IGrantIntentStore store, GrantedPathEntry entry)
    {
        var ownerSid = ownerResolver.ResolveStorageOwnerSid(sid);
        if (!ownerResolver.UsesSharedContainerTraverse(sid))
            return store.RemoveEntry(ownerSid, entry);

        if (entry.SourceSids == null)
            return false;

        var remainingSources = entry.SourceSids
            .Where(sourceSid => !string.Equals(sourceSid, sid, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (remainingSources.Count == entry.SourceSids.Count)
            return false;

        if (remainingSources.Count == 0)
            return store.RemoveEntry(ownerSid, entry);

        var replacement = entry.Clone();
        replacement.SourceSids = remainingSources;
        return store.ReplaceEntry(ownerSid, entry, replacement);
    }
}
