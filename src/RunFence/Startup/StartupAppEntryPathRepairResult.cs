namespace RunFence.Startup;

public sealed record StartupAppEntryPathRepairResult(
    bool Changed,
    IReadOnlyList<string> ChangedAppIds,
    IReadOnlyList<string> Warnings,
    string? SaveFailureMessage);
