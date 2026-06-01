using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Acl.UI;

public class AclManagerPendingGrantState
{
    private static readonly GrantPathKeyComparer PathKeyComparer = new();
    private readonly PendingPathChangeCollection<(string Path, bool IsDeny)> _grantChanges;
    private readonly Dictionary<(string Path, bool IsDeny), PendingModification> _pendingModifications;

    internal AclManagerPendingGrantState(
        PendingPathChangeCollection<(string Path, bool IsDeny)> grantChanges,
        Dictionary<(string Path, bool IsDeny), PendingModification> pendingModifications)
    {
        _grantChanges = grantChanges;
        _pendingModifications = pendingModifications;
    }

    public bool IsPendingAdd(string path, bool isDeny) => _grantChanges.ContainsAdd(AclPendingKeys.Grant(path, isDeny));

    public GrantedPathEntry? FindPendingAdd(string path, bool isDeny) =>
        _grantChanges.GetAdd(AclPendingKeys.Grant(path, isDeny));

    public bool IsPendingRemove(string path, bool isDeny) => _grantChanges.ContainsRemove(AclPendingKeys.Grant(path, isDeny));

    public bool IsPendingModification(string path, bool isDeny) =>
        TryGetPendingModification(path, isDeny, out _) ||
        EnumerateModifications().Any(m =>
            string.Equals(m.Entry.Path, path, StringComparison.OrdinalIgnoreCase) &&
            (m.Entry.IsDeny == isDeny || m.NewIsDeny == isDeny || m.WasIsDeny == isDeny));

    public bool IsPendingGrantFix(string path, bool isDeny) => _grantChanges.ContainsFix(AclPendingKeys.Grant(path, isDeny));

    public bool IsUntrackGrant(string path, bool isDeny) => _grantChanges.ContainsUntrack(AclPendingKeys.Grant(path, isDeny));

    public bool TryGetPendingModification(string path, bool isDeny, out PendingModification? modification)
    {
        if (_pendingModifications.TryGetValue(AclPendingKeys.Grant(path, isDeny), out var storedModification))
        {
            modification = CloneModification(storedModification);
            return true;
        }

        modification = null;
        return false;
    }

    public bool IsPendingConfigMove(string path, bool isDeny)
    {
        if (_grantChanges.ContainsConfigMove(AclPendingKeys.Grant(path, isDeny)))
            return true;

        var mod = FindModificationForPathAndMode(path, isDeny);
        return mod != null && _grantChanges.ContainsConfigMove(AclPendingKeys.Grant(path, mod.NewIsDeny));
    }

    public bool TryGetPendingConfigMove(string path, bool isDeny, out PendingConfigMove? move)
        => _grantChanges.TryGetConfigMove(AclPendingKeys.Grant(path, isDeny), out move);

    public bool GetEffectiveIsDeny(GrantedPathEntry entry)
    {
        if (TryGetPendingModification(entry.Path, entry.IsDeny, out var mod))
            return mod!.NewIsDeny;

        mod = FindModificationForEntry(entry);
        return mod?.NewIsDeny ?? entry.IsDeny;
    }

    public SavedRightsState? GetEffectiveRights(GrantedPathEntry entry)
    {
        if (TryGetPendingModification(entry.Path, entry.IsDeny, out var mod) && mod!.NewRights != null)
            return mod.NewRights;

        mod = FindModificationForEntry(entry);
        return mod?.NewRights ?? entry.SavedRights;
    }

    public string? GetEffectiveConfigPath(
        GrantedPathEntry entry,
        IGrantIntentRepository grantIntentRepository,
        IGrantIntentStoreProvider grantIntentStoreProvider,
        string sid)
    {
        var effectiveIsDeny = GetEffectiveIsDeny(entry);
        if (TryGetPendingConfigMove(Path.GetFullPath(entry.Path), effectiveIsDeny, out var pendingMove))
            return pendingMove!.TargetConfigPath;

        var location = grantIntentRepository.FindGrant(sid, entry);
        return grantIntentStoreProvider.ResolveStore(location?.Store.ConfigPath).ConfigPath;
    }

    public bool ExistsInDbOrPending(AppDatabase database, string sid, string normalizedPath, bool isDeny)
    {
        if (IsPendingAdd(normalizedPath, isDeny))
            return true;

        var entries = database.GetAccount(sid)?.Grants;
        return entries != null &&
               entries.Any(e => string.Equals(e.Path, normalizedPath, StringComparison.OrdinalIgnoreCase) &&
                                e.IsDeny == isDeny &&
                                !e.IsTraverseOnly &&
                                !IsPendingRemove(normalizedPath, isDeny) &&
                                !IsUntrackGrant(normalizedPath, isDeny));
    }

    public AclGrantPendingChangesSnapshot GetSnapshot()
        => new(
            _grantChanges.GetAddsSnapshot(),
            _grantChanges.GetRemovesSnapshot(),
            CloneModificationDictionary(_pendingModifications),
            _grantChanges.GetFixesSnapshot(),
            _grantChanges.GetUntrackSnapshot(),
            _grantChanges.GetConfigMovesSnapshot());

    public IReadOnlyDictionary<(string Path, bool IsDeny), GrantedPathEntry> GetPendingAddsSnapshot()
        => _grantChanges.GetAddsSnapshot();

    public IReadOnlyDictionary<(string Path, bool IsDeny), GrantedPathEntry> GetPendingRemovesSnapshot()
        => _grantChanges.GetRemovesSnapshot();

    public IReadOnlyDictionary<(string Path, bool IsDeny), PendingModification> GetPendingModificationsSnapshot()
        => CloneModificationDictionary(_pendingModifications);

    public IReadOnlyDictionary<(string Path, bool IsDeny), GrantedPathEntry> GetPendingGrantFixesSnapshot()
        => _grantChanges.GetFixesSnapshot();

    public IReadOnlyDictionary<(string Path, bool IsDeny), GrantedPathEntry> GetPendingUntrackSnapshot()
        => _grantChanges.GetUntrackSnapshot();

    public IReadOnlyDictionary<(string Path, bool IsDeny), PendingConfigMove> GetPendingConfigMovesSnapshot()
        => _grantChanges.GetConfigMovesSnapshot();

    internal bool HasPendingChanges => _grantChanges.HasPendingChanges || _pendingModifications.Count > 0;

    internal void Clear()
    {
        _grantChanges.Clear();
        _pendingModifications.Clear();
    }

    internal void RestoreFromSnapshot(AclGrantPendingChangesSnapshot snapshot)
    {
        _grantChanges.Restore(
            snapshot.PendingAdds,
            snapshot.PendingRemoves,
            snapshot.PendingGrantFixes,
            snapshot.PendingUntrack,
            snapshot.PendingConfigMoves);
        RestoreModificationDictionary(_pendingModifications, snapshot.PendingModifications);
    }

    private PendingModification? FindModificationForEntry(GrantedPathEntry entry)
        => FindModificationForPathAndMode(entry.Path, entry.IsDeny);

    private PendingModification? FindModificationForPathAndMode(string path, bool isDeny)
        => EnumerateModifications().FirstOrDefault(m =>
            string.Equals(m.Entry.Path, path, StringComparison.OrdinalIgnoreCase) &&
            (m.Entry.IsDeny == isDeny || m.WasIsDeny == isDeny || m.NewIsDeny == isDeny));

    private IEnumerable<PendingModification> EnumerateModifications()
        => _pendingModifications.Values.Select(CloneModification);

    private static Dictionary<(string Path, bool IsDeny), PendingModification> CloneModificationDictionary(
        IReadOnlyDictionary<(string Path, bool IsDeny), PendingModification> source)
        => source.ToDictionary(item => item.Key, item => CloneModification(item.Value), PathKeyComparer);

    private static PendingModification CloneModification(PendingModification modification)
        => modification with { Entry = modification.Entry.Clone() };

    private static void RestoreModificationDictionary<TKey>(
        Dictionary<TKey, PendingModification> target,
        IReadOnlyDictionary<TKey, PendingModification> source)
        where TKey : notnull
        => RestoreDictionary(target, source, CloneModification);

    private static void RestoreDictionary<TKey, TValue>(
        Dictionary<TKey, TValue> target,
        IReadOnlyDictionary<TKey, TValue> source,
        Func<TValue, TValue> clone)
        where TKey : notnull
    {
        target.Clear();
        foreach (var item in source)
            target[item.Key] = clone(item.Value);
    }
}
