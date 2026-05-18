using RunFence.Core.Models;

namespace RunFence.Acl.Traverse;

public interface ITraverseGrantOwnerResolver
{
    bool UsesSharedContainerTraverse(string sid);

    string ResolveStorageOwnerSid(string sid);

    string ResolveAclSid(string sid);

    List<GrantedPathEntry> GetOrCreateTraverseStore(AppDatabase database, string sid);

    List<GrantedPathEntry> GetTraverseStoreOrEmpty(AppDatabase database, string sid);

    IEnumerable<AccountEntry> GetGrantOwnersForTraverseCleanup(AppDatabase database, string sid);

    bool EntryAppliesToSid(GrantedPathEntry entry, string sid, bool includeManualSharedEntries);

    GrantedPathEntry? FindTraverseEntry(
        AppDatabase database,
        string sid,
        string normalizedPath,
        bool includeManualSharedEntries = false);
}
