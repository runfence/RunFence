using RunFence.Core.Models;

namespace RunFence.SidMigration;

public sealed class SidMigrationDataMappingPlanner(
    SidMigrationCoreMutationService coreMutationService)
{
    public SidMigrationDataMappingPlan BuildPlan(
        IReadOnlyList<SidMigrationMapping> mappings,
        CredentialStore credentialStore,
        AppDatabase database)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        ArgumentNullException.ThrowIfNull(credentialStore);
        ArgumentNullException.ThrowIfNull(database);

        if (mappings.Count == 0)
            return new SidMigrationDataMappingPlan([], default);

        var plannedMappings = mappings.ToList().AsReadOnly();
        var credentialSnapshot = credentialStore.CreateSnapshot();
        var databaseSnapshot = database.CreateSnapshot();
        var counts = coreMutationService.ApplyCoreMappings(
            plannedMappings,
            credentialSnapshot,
            databaseSnapshot);

        return new SidMigrationDataMappingPlan(plannedMappings, counts);
    }

    public string FormatMigrationMessage(MigrationCounts counts)
        => $"Migrated {counts.Credentials} credential(s), {counts.Apps} app(s), {counts.IpcCallers} IPC caller(s), {counts.AllowEntries} allow entry/entries.";

}

public sealed record SidMigrationDataMappingPlan(
    IReadOnlyList<SidMigrationMapping> Mappings,
    MigrationCounts Counts);
