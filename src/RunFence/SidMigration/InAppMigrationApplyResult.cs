namespace RunFence.SidMigration;

public readonly record struct InAppMigrationApplyResult(
    IReadOnlyList<string> Messages,
    bool Success,
    string? SaveError);
