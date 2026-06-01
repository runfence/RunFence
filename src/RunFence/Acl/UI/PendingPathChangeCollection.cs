using RunFence.Core.Models;

namespace RunFence.Acl.UI;

internal sealed class PendingPathChangeCollection<TKey>
    where TKey : notnull
{
    private readonly Func<GrantedPathEntry, TKey> _keySelector;
    private readonly IEqualityComparer<TKey> _comparer;
    private readonly Dictionary<TKey, GrantedPathEntry> _pendingAdds;
    private readonly Dictionary<TKey, GrantedPathEntry> _pendingRemoves;
    private readonly Dictionary<TKey, GrantedPathEntry> _pendingFixes;
    private readonly Dictionary<TKey, GrantedPathEntry> _pendingUntrack;
    private readonly Dictionary<TKey, PendingConfigMove> _pendingConfigMoves;

    public PendingPathChangeCollection(
        Func<GrantedPathEntry, TKey> keySelector,
        IEqualityComparer<TKey> comparer)
    {
        _keySelector = keySelector;
        _comparer = comparer;
        _pendingAdds = new Dictionary<TKey, GrantedPathEntry>(comparer);
        _pendingRemoves = new Dictionary<TKey, GrantedPathEntry>(comparer);
        _pendingFixes = new Dictionary<TKey, GrantedPathEntry>(comparer);
        _pendingUntrack = new Dictionary<TKey, GrantedPathEntry>(comparer);
        _pendingConfigMoves = new Dictionary<TKey, PendingConfigMove>(comparer);
    }

    public bool HasPendingChanges =>
        _pendingAdds.Count + _pendingRemoves.Count + _pendingFixes.Count +
        _pendingUntrack.Count + _pendingConfigMoves.Count > 0;

    public void Clear()
    {
        _pendingAdds.Clear();
        _pendingRemoves.Clear();
        _pendingFixes.Clear();
        _pendingUntrack.Clear();
        _pendingConfigMoves.Clear();
    }

    public void Add(GrantedPathEntry entry)
        => _pendingAdds[_keySelector(entry)] = entry;

    public bool RemoveAdd(TKey key)
        => _pendingAdds.Remove(key);

    public bool ContainsAdd(TKey key)
        => _pendingAdds.ContainsKey(key);

    public GrantedPathEntry? GetAdd(TKey key)
        => _pendingAdds.GetValueOrDefault(key);

    public void AddRemoval(GrantedPathEntry entry)
        => _pendingRemoves[_keySelector(entry)] = entry;

    public bool RemoveRemoval(TKey key)
        => _pendingRemoves.Remove(key);

    public bool ContainsRemove(TKey key)
        => _pendingRemoves.ContainsKey(key);

    public void AddFix(GrantedPathEntry entry)
        => _pendingFixes[_keySelector(entry)] = entry;

    public bool RemoveFix(TKey key)
        => _pendingFixes.Remove(key);

    public bool ContainsFix(TKey key)
        => _pendingFixes.ContainsKey(key);

    public void AddUntrack(GrantedPathEntry entry)
        => _pendingUntrack[_keySelector(entry)] = entry;

    public bool RemoveUntrack(TKey key)
        => _pendingUntrack.Remove(key);

    public bool ContainsUntrack(TKey key)
        => _pendingUntrack.ContainsKey(key);

    public void AddConfigMove(GrantedPathEntry entry, string? targetConfigPath)
        => _pendingConfigMoves[_keySelector(entry)] = new PendingConfigMove(entry, targetConfigPath);

    public bool RemoveConfigMove(TKey key, out PendingConfigMove? move)
        => _pendingConfigMoves.Remove(key, out move);

    public bool TryGetConfigMove(TKey key, out PendingConfigMove? move)
    {
        if (_pendingConfigMoves.TryGetValue(key, out var storedMove))
        {
            move = CloneConfigMove(storedMove);
            return true;
        }

        move = null;
        return false;
    }

    public bool ContainsConfigMove(TKey key)
        => _pendingConfigMoves.ContainsKey(key);

    public bool RekeyConfigMove(TKey currentKey, GrantedPathEntry updatedEntry)
    {
        if (!_pendingConfigMoves.Remove(currentKey, out var move))
            return false;

        AddConfigMove(updatedEntry, move.TargetConfigPath);
        return true;
    }

    public IReadOnlyDictionary<TKey, GrantedPathEntry> GetAddsSnapshot()
        => CloneEntryDictionary(_pendingAdds);

    public IReadOnlyDictionary<TKey, GrantedPathEntry> GetRemovesSnapshot()
        => CloneEntryDictionary(_pendingRemoves);

    public IReadOnlyDictionary<TKey, GrantedPathEntry> GetFixesSnapshot()
        => CloneEntryDictionary(_pendingFixes);

    public IReadOnlyDictionary<TKey, GrantedPathEntry> GetUntrackSnapshot()
        => CloneEntryDictionary(_pendingUntrack);

    public IReadOnlyDictionary<TKey, PendingConfigMove> GetConfigMovesSnapshot()
        => CloneConfigMoveDictionary(_pendingConfigMoves);

    public void Restore(
        IReadOnlyDictionary<TKey, GrantedPathEntry> adds,
        IReadOnlyDictionary<TKey, GrantedPathEntry> removes,
        IReadOnlyDictionary<TKey, GrantedPathEntry> fixes,
        IReadOnlyDictionary<TKey, GrantedPathEntry> untrack,
        IReadOnlyDictionary<TKey, PendingConfigMove> configMoves)
    {
        RestoreEntryDictionary(_pendingAdds, adds);
        RestoreEntryDictionary(_pendingRemoves, removes);
        RestoreEntryDictionary(_pendingFixes, fixes);
        RestoreEntryDictionary(_pendingUntrack, untrack);
        RestoreConfigMoveDictionary(_pendingConfigMoves, configMoves);
    }

    private Dictionary<TKey, GrantedPathEntry> CloneEntryDictionary(
        IReadOnlyDictionary<TKey, GrantedPathEntry> source)
        => source.ToDictionary(item => item.Key, item => item.Value.Clone(), _comparer);

    private Dictionary<TKey, PendingConfigMove> CloneConfigMoveDictionary(
        IReadOnlyDictionary<TKey, PendingConfigMove> source)
        => source.ToDictionary(item => item.Key, item => CloneConfigMove(item.Value), _comparer);

    private static PendingConfigMove CloneConfigMove(PendingConfigMove move)
        => move with { Entry = move.Entry.Clone() };

    private static void RestoreEntryDictionary(
        Dictionary<TKey, GrantedPathEntry> target,
        IReadOnlyDictionary<TKey, GrantedPathEntry> source)
        => RestoreDictionary(target, source, entry => entry.Clone());

    private static void RestoreConfigMoveDictionary(
        Dictionary<TKey, PendingConfigMove> target,
        IReadOnlyDictionary<TKey, PendingConfigMove> source)
        => RestoreDictionary(target, source, CloneConfigMove);

    private static void RestoreDictionary<TValue>(
        Dictionary<TKey, TValue> target,
        IReadOnlyDictionary<TKey, TValue> source,
        Func<TValue, TValue> clone)
    {
        target.Clear();
        foreach (var item in source)
            target[item.Key] = clone(item.Value);
    }
}
