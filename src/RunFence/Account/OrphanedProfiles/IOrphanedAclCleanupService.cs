namespace RunFence.Account.OrphanedProfiles;

public record AclCleanupProgress(string CurrentPath, int ObjectsFixed, int ObjectsScanned);

public interface IOrphanedAclCleanupService
{
    Task<List<(string Path, string Action, string? Error)>> CleanupAclReferencesAsync(
        List<string> sids, IProgress<AclCleanupProgress>? progress, CancellationToken ct);
}