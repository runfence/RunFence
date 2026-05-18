namespace RunFence.SidMigration;

public record SidMigrationWorkflowResult(
    SidMigrationWorkflowStatus Status,
    bool AppliedFilesystemChanges,
    bool AppliedAppEnforcementChanges,
    bool SavedDatabase,
    bool RetryStateWritten,
    IReadOnlyList<string> Errors);
