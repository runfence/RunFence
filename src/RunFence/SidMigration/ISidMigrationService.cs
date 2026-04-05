using RunFence.Core.Models;

namespace RunFence.SidMigration;

public interface ISidMigrationService
{
    List<SidMigrationMapping> BuildMappings(
        IReadOnlyList<CredentialEntry> credentials,
        IReadOnlyList<LocalUserAccount> currentLocalAccounts,
        IReadOnlyDictionary<string, string>? sidNames = null);

    Task<List<OrphanedSid>> DiscoverOrphanedSidsAsync(
        IReadOnlyList<string> rootPaths,
        IProgress<(long scanned, long sidsFound)> onProgress,
        CancellationToken ct);

    Task<List<SidMigrationMatch>> ScanAsync(
        IReadOnlyList<string> rootPaths,
        IReadOnlyList<SidMigrationMapping> mappings,
        IProgress<(long scanned, long found)> onProgress,
        CancellationToken ct);

    Task<(long applied, long errors)> ApplyAsync(
        IReadOnlyList<SidMigrationMatch> hits,
        IReadOnlyList<SidMigrationMapping> mappings,
        IProgress<MigrationProgress> onProgress,
        CancellationToken ct);

    MigrationCounts MigrateAppData(
        IReadOnlyList<SidMigrationMapping> mappings,
        CredentialStore credentialStore);

    (int credentials, int apps, int ipcCallers) DeleteSidsFromAppData(
        IReadOnlyList<string> sidsToDelete,
        CredentialStore credentialStore);
}