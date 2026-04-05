namespace RunFence.SidMigration;

public readonly record struct MigrationProgress(long Applied, long Total, string CurrentPath);