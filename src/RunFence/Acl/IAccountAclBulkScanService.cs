namespace RunFence.Acl;

public record AccountScanResult(
    List<DiscoveredGrant> Grants,
    List<string> TraversePaths);

public record DiscoveredGrant(
    string Path,
    bool IsDeny,
    bool Execute,
    bool Write,
    bool Read,
    bool Special,
    bool IsOwner);

/// <summary>
/// Scans a folder tree for explicit NTFS ACEs belonging to any of a known set of SIDs,
/// building per-account scan results for bulk import into the ACL Manager.
/// </summary>
public interface IAccountAclBulkScanService
{
    /// <summary>
    /// Traverses <paramref name="rootPath"/> and collects explicit ACEs for all SIDs in
    /// <paramref name="knownSids"/>. Returns a dictionary keyed by SID string, containing
    /// discovered grants and traverse-only paths for each account.
    /// </summary>
    Task<Dictionary<string, AccountScanResult>> ScanAllAccountsAsync(
        string rootPath,
        IReadOnlySet<string> knownSids,
        IProgress<long> progress,
        CancellationToken ct);
}