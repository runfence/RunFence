namespace RunFence.Core.Models;

public sealed record ShortcutProtectionState(
    string ShortcutPath,
    bool ManagedDenyAceApplied,
    bool WasReadOnlyBeforeProtection,
    bool ReadOnlySetByRunFence);
