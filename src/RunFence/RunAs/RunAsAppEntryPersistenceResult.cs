using RunFence.Core.Models;

namespace RunFence.RunAs;

public sealed record RunAsAppEntryPersistenceResult(
    RunAsAppEntryPersistenceStatus Status,
    AppEntry? AppEntry,
    string? ErrorMessage = null,
    string? WarningMessage = null);
