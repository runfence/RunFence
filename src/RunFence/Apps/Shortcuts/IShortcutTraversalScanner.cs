namespace RunFence.Apps.Shortcuts;

internal interface IShortcutTraversalScanner
{
    IEnumerable<ShortcutTraversalEntry> ScanShortcuts(HashSet<string>? managedSids);
}
