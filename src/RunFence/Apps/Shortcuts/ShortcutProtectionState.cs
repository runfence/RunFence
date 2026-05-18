namespace RunFence.Apps.Shortcuts;

public sealed record ShortcutProtectionState(
    string ShortcutPath,
    bool ManagedDenyAceApplied,
    bool WasReadOnlyBeforeProtection,
    bool ReadOnlySetByRunFence);
