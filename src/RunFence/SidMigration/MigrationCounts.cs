namespace RunFence.SidMigration;

public readonly record struct MigrationCounts(int Credentials, int Apps, int IpcCallers, int AllowEntries);