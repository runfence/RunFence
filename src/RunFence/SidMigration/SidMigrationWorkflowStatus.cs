namespace RunFence.SidMigration;

public enum SidMigrationWorkflowStatus
{
    Succeeded,
    Canceled,
    AppliedButSaveFailed,
    Failed,
    RollbackFailed
}
