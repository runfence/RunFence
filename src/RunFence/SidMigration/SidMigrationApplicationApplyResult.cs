namespace RunFence.SidMigration;

public readonly record struct SidMigrationApplicationApplyResult(
    SidMigrationWorkflowResult Workflow,
    IReadOnlyList<string> Messages,
    string? SaveError);
