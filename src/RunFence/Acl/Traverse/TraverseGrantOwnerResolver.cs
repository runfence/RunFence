using RunFence.Core.Models;

namespace RunFence.Acl.Traverse;

public class TraverseGrantOwnerResolver : ITraverseGrantOwnerResolver
{
    public bool UsesSharedContainerTraverse(string sid)
        => AclHelper.IsSpecificContainerSid(sid);

    public string ResolveStorageOwnerSid(string sid)
        => TraverseEntryLookup.ResolveStorageOwnerSid(sid);

    public string ResolveAclSid(string sid)
        => TraverseEntryLookup.ResolveAclSid(sid);

    public List<GrantedPathEntry> GetOrCreateTraverseStore(AppDatabase database, string sid)
        => TraverseEntryLookup.GetOrCreateTraverseStore(database, sid);

    public List<GrantedPathEntry> GetTraverseStoreOrEmpty(AppDatabase database, string sid)
        => TraverseEntryLookup.GetTraverseStoreOrEmpty(database, sid);

    public IEnumerable<AccountEntry> GetGrantOwnersForTraverseCleanup(AppDatabase database, string sid)
        => UsesSharedContainerTraverse(sid)
            ? database.Accounts.Where(account => AclHelper.IsSpecificContainerSid(account.Sid))
            : database.GetAccount(sid) is { } account ? [account] : [];

    public bool EntryAppliesToSid(GrantedPathEntry entry, string sid, bool includeManualSharedEntries)
        => TraverseEntryLookup.EntryAppliesToSid(entry, sid, includeManualSharedEntries);

    public GrantedPathEntry? FindTraverseEntry(
        AppDatabase database,
        string sid,
        string normalizedPath,
        bool includeManualSharedEntries = false)
        => TraverseEntryLookup.FindTraverseEntryInDb(database, sid, normalizedPath, includeManualSharedEntries);

    public void RestoreTraverseEntry(
        AppDatabase database,
        string sid,
        string normalizedPath,
        GrantedPathEntry? snapshot)
    {
        var entries = snapshot != null
            ? GetOrCreateTraverseStore(database, sid)
            : GetTraverseStoreOrEmpty(database, sid);
        var currentEntry = FindTraverseEntry(
            database,
            sid,
            normalizedPath,
            includeManualSharedEntries: true);
        if (currentEntry != null)
            entries.Remove(currentEntry);

        if (snapshot != null)
        {
            var existingSnapshotEntry = entries.FirstOrDefault(entry => SameTraverseIdentity(entry, snapshot));
            if (existingSnapshotEntry != null)
                entries.Remove(existingSnapshotEntry);

            entries.Add(snapshot.Clone());
        }
    }

    private static bool SameTraverseIdentity(GrantedPathEntry entry, GrantedPathEntry snapshot)
        => entry.IsTraverseOnly &&
           snapshot.IsTraverseOnly &&
           string.Equals(entry.Path, snapshot.Path, StringComparison.OrdinalIgnoreCase) &&
           SameSidSet(entry.SourceSids, snapshot.SourceSids);

    private static bool SameSidSet(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
    {
        if (left == null || right == null)
            return left == right;

        return left.Count == right.Count &&
               left.All(sid => right.Contains(sid, StringComparer.OrdinalIgnoreCase));
    }
}
