using RunFence.Core.Models;

namespace RunFence.Apps.Shortcuts;

public record ShortcutContext(
    string OriginalLnkPath,
    bool IsAlreadyManaged,
    AppEntry? ManagedApp);