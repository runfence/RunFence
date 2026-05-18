using RunFence.Core.Models;

namespace RunFence.Acl.Traverse;

public class TraverseGrantOwnerResolver : ITraverseGrantOwnerResolver
{
    public bool UsesSharedContainerTraverse(string sid)
        => AclHelper.IsSpecificContainerSid(sid);

    public string ResolveStorageOwnerSid(string sid)
        => UsesSharedContainerTraverse(sid)
            ? AclHelper.AllApplicationPackagesSid
            : sid;

    public string ResolveAclSid(string sid)
        => UsesSharedContainerTraverse(sid)
            ? AclHelper.AllApplicationPackagesSid
            : sid;

    public List<GrantedPathEntry> GetOrCreateTraverseStore(AppDatabase database, string sid)
        => database.GetOrCreateAccount(ResolveStorageOwnerSid(sid)).Grants;

    public List<GrantedPathEntry> GetTraverseStoreOrEmpty(AppDatabase database, string sid)
        => database.GetAccount(ResolveStorageOwnerSid(sid))?.Grants ?? [];

    public IEnumerable<AccountEntry> GetGrantOwnersForTraverseCleanup(AppDatabase database, string sid)
        => UsesSharedContainerTraverse(sid)
            ? database.Accounts.Where(account => AclHelper.IsSpecificContainerSid(account.Sid))
            : database.GetAccount(sid) is { } account ? [account] : [];

    public bool EntryAppliesToSid(GrantedPathEntry entry, string sid, bool includeManualSharedEntries)
    {
        if (!UsesSharedContainerTraverse(sid))
            return true;

        if (entry.SourceSids == null)
            return includeManualSharedEntries;

        return entry.SourceSids.Contains(sid, StringComparer.OrdinalIgnoreCase);
    }

    public GrantedPathEntry? FindTraverseEntry(
        AppDatabase database,
        string sid,
        string normalizedPath,
        bool includeManualSharedEntries = false)
    {
        var matches = GetTraverseStoreOrEmpty(database, sid)
            .Where(entry =>
                entry.IsTraverseOnly &&
                string.Equals(entry.Path, normalizedPath, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (!UsesSharedContainerTraverse(sid))
            return matches.FirstOrDefault();

        var sourceTrackedEntry = matches.FirstOrDefault(entry =>
            entry.SourceSids?.Contains(sid, StringComparer.OrdinalIgnoreCase) == true);
        if (sourceTrackedEntry != null)
            return sourceTrackedEntry;

        if (!includeManualSharedEntries)
            return null;

        return matches.FirstOrDefault(entry => entry.SourceSids == null);
    }
}
