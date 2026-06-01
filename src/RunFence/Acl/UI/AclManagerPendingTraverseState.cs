using RunFence.Acl;
using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Acl.UI;

public class AclManagerPendingTraverseState
{
    private readonly PendingPathChangeCollection<string> _traverseChanges;

    internal AclManagerPendingTraverseState(PendingPathChangeCollection<string> traverseChanges)
    {
        _traverseChanges = traverseChanges;
    }

    public bool IsPendingTraverseAdd(string path) => _traverseChanges.ContainsAdd(AclPendingKeys.Traverse(path));

    public bool IsPendingTraverseRemove(string path) => _traverseChanges.ContainsRemove(AclPendingKeys.Traverse(path));

    public bool IsUntrackTraverse(string path) => _traverseChanges.ContainsUntrack(AclPendingKeys.Traverse(path));

    public bool IsPendingTraverseConfigMove(string path) => _traverseChanges.ContainsConfigMove(AclPendingKeys.Traverse(path));

    public bool TryGetPendingTraverseConfigMove(string path, out PendingConfigMove? move)
        => _traverseChanges.TryGetConfigMove(AclPendingKeys.Traverse(path), out move);

    public string? GetEffectiveConfigPath(
        GrantedPathEntry entry,
        IGrantIntentRepository grantIntentRepository,
        IGrantIntentStoreProvider grantIntentStoreProvider,
        string sid)
    {
        var normalizedPath = Path.GetFullPath(entry.Path);
        if (TryGetPendingTraverseConfigMove(normalizedPath, out var pendingMove))
            return grantIntentStoreProvider.ResolveStore(pendingMove!.TargetConfigPath).ConfigPath;

        var lookupSid = TraverseEntryLookup.ResolveStorageOwnerSid(sid);
        var location = grantIntentRepository.FindTraverse(lookupSid, entry);
        return grantIntentStoreProvider.ResolveStore(location?.Store.ConfigPath).ConfigPath;
    }

    public bool ExistsTraverseInDbOrPending(AppDatabase database, string sid, string normalizedPath, bool checkUntrack = true)
    {
        if (IsPendingTraverseAdd(normalizedPath))
            return true;

        var entries = database.GetAccount(TraverseEntryLookup.ResolveStorageOwnerSid(sid))?.Grants;
        return entries != null &&
               entries.Any(e => e.IsTraverseOnly &&
                                string.Equals(e.Path, normalizedPath, StringComparison.OrdinalIgnoreCase) &&
                                TraverseEntryLookup.EntryAppliesToSid(e, sid, includeManualSharedEntries: true) &&
                                !IsPendingTraverseRemove(normalizedPath) &&
                                (!checkUntrack || !IsUntrackTraverse(normalizedPath)));
    }

    public AclTraversePendingChangesSnapshot GetSnapshot()
        => new(
            _traverseChanges.GetAddsSnapshot(),
            _traverseChanges.GetRemovesSnapshot(),
            _traverseChanges.GetFixesSnapshot(),
            _traverseChanges.GetUntrackSnapshot(),
            _traverseChanges.GetConfigMovesSnapshot());

    public IReadOnlyDictionary<string, GrantedPathEntry> GetPendingAddsSnapshot()
        => _traverseChanges.GetAddsSnapshot();

    public IReadOnlyDictionary<string, GrantedPathEntry> GetPendingRemovesSnapshot()
        => _traverseChanges.GetRemovesSnapshot();

    public IReadOnlyDictionary<string, GrantedPathEntry> GetPendingFixesSnapshot()
        => _traverseChanges.GetFixesSnapshot();

    public IReadOnlyDictionary<string, GrantedPathEntry> GetPendingUntrackSnapshot()
        => _traverseChanges.GetUntrackSnapshot();

    public IReadOnlyDictionary<string, PendingConfigMove> GetPendingConfigMovesSnapshot()
        => _traverseChanges.GetConfigMovesSnapshot();

    internal bool HasPendingChanges => _traverseChanges.HasPendingChanges;

    internal void Clear()
        => _traverseChanges.Clear();

    internal void RestoreFromSnapshot(AclTraversePendingChangesSnapshot snapshot)
        => _traverseChanges.Restore(
            snapshot.PendingAdds,
            snapshot.PendingRemoves,
            snapshot.PendingFixes,
            snapshot.PendingUntrack,
            snapshot.PendingConfigMoves);
}
