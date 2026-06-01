namespace RunFence.SidMigration;

public sealed class SidMigrationApplyState
{
    public bool AppEnforcementApplied { get; set; }

    public bool FilesystemChangesApplied { get; set; }

    public bool ExternalMutationStarted { get; set; }

    public bool PostMutationFailure { get; set; }

    public List<string> Messages { get; } = [];

    public List<string> Errors { get; } = [];
}
