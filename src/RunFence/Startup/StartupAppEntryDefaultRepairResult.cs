namespace RunFence.Startup;

public sealed record StartupAppEntryDefaultRepairResult(
    bool Changed,
    IReadOnlyList<string> ChangedAppIds,
    IReadOnlyList<string> Warnings);
