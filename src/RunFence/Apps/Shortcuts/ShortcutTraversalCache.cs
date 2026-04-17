namespace RunFence.Apps.Shortcuts;

public sealed class ShortcutTraversalCache
{
    private readonly OrderedDictionary<string, ShortcutTraversalEntry> _entries;

    public ShortcutTraversalCache(IEnumerable<ShortcutTraversalEntry> entries)
    {
        _entries = new OrderedDictionary<string, ShortcutTraversalEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
            _entries[entry.Path] = entry;
    }

    public IReadOnlyList<ShortcutTraversalEntry> Entries => _entries.Values;

    public List<string> FindWhere(Func<string?, string?, bool> predicate)
    {
        return _entries
            .Values
            .Where(entry => predicate(entry.TargetPath, entry.Arguments))
            .Select(entry => entry.Path)
            .ToList();
    }

    public void RecordShortcut(string path, string? targetPath, string? arguments)
    {
        _entries[path] = new ShortcutTraversalEntry(path, targetPath, arguments);
    }

    public void RemoveShortcut(string path)
    {
        _entries.Remove(path);
    }
}
