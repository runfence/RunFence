using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Acl.Traverse;

public interface ITraverseIntentStoreCoordinator
{
    bool UsesSharedContainerTraverse(string sid);

    string ResolveStorageOwnerSid(string sid);

    string ResolveAclSid(string sid);

    List<GrantedPathEntry> GetOrCreateTraverseStore(AppDatabase database, string sid);

    List<GrantedPathEntry> GetTraverseStoreOrEmpty(AppDatabase database, string sid);

    IEnumerable<AccountEntry> GetGrantOwnersForTraverseCleanup(AppDatabase database, string sid);

    List<GrantIntentLocation> GetTraverseLocationsForPath(string sid, string normalizedPath, bool includeManualSharedEntries);

    List<GrantIntentLocation> GetAllTraverseLocations(string sid);

    GrantedPathEntry BuildTraverseEntry(string sid, string normalizedPath, IReadOnlyList<string> coveragePaths, GrantedPathEntry? currentEntry);

    bool RemoveTraverseEntryFromStore(string sid, IGrantIntentStore store, GrantedPathEntry entry);
}
