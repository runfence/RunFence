using System.Security.AccessControl;
using System.Security.Principal;

namespace RunFence.Acl;

/// <summary>
/// Scans a folder tree for explicit NTFS ACEs belonging to any known SID,
/// building per-account results for bulk import into the ACL Manager.
/// </summary>
public class AccountAclBulkScanService(
    IFileSystemAclTraverser traverser,
    IAclAccessor aclAccessor) : IAccountAclBulkScanService
{
    /// <summary>
    /// Accumulated NTFS rights for a single path during a bulk ACL scan.
    /// OR-merged across multiple ACEs for the same SID+path.
    /// </summary>
    private record struct PathAclAccumulator(
        bool IsDirectory,
        bool HasAllow,
        bool HasDeny,
        FileSystemRights AllowRights,
        FileSystemRights DenyRights,
        bool IsAccountOwner);

    public Task<Dictionary<string, AccountScanResult>> ScanAllAccountsAsync(
        string rootPath,
        IReadOnlySet<string> knownSids,
        IProgress<long> progress,
        CancellationToken ct)
    {
        return Task.Run(() => Scan(rootPath, knownSids, progress, ct), ct);
    }

    private Dictionary<string, AccountScanResult> Scan(
        string rootPath,
        IReadOnlySet<string> knownSids,
        IProgress<long> progress,
        CancellationToken ct)
    {
        ValidateRootAccessibility(rootPath);

        // Per-SID: accumulated rights per path (OR-merged across multiple ACEs for the same SID+path)
        var grantRights = new Dictionary<string, Dictionary<string, PathAclAccumulator>>(StringComparer.OrdinalIgnoreCase);

        // Per-SID: traverse-only paths seen
        var traverseSeen = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var sid in knownSids)
        {
            grantRights[sid] = new Dictionary<string, PathAclAccumulator>(StringComparer.OrdinalIgnoreCase);
            traverseSeen[sid] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        // Privileges (SeBackup/SeRestore/SeTakeOwnership) are enabled once at startup.
        foreach (var (path, isDirectory, security) in traverser.Traverse([rootPath], progress, ct))
        {
            var rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier));

            // Determine owner SID for this path
            var ownerSid = security.GetOwner(typeof(SecurityIdentifier))?.Value;

            foreach (FileSystemAccessRule rule in rules)
            {
                var ruleSidValue = rule.IdentityReference.Value;
                if (!knownSids.Contains(ruleSidValue))
                    continue;

                bool isDeny = rule.AccessControlType == AccessControlType.Deny;
                bool isTraverseOnly = !isDeny && GrantRightsMapper.IsTraverseOnly(rule.FileSystemRights);

                if (isTraverseOnly)
                {
                    traverseSeen[ruleSidValue].Add(path);
                }
                else
                {
                    var perSidGrants = grantRights[ruleSidValue];
                    perSidGrants.TryGetValue(path, out var existing);
                    bool isOwner = string.Equals(ownerSid, ruleSidValue, StringComparison.OrdinalIgnoreCase);
                    if (isDeny)
                        perSidGrants[path] = new PathAclAccumulator(
                            IsDirectory: isDirectory,
                            HasAllow: existing.HasAllow,
                            HasDeny: true,
                            AllowRights: existing.AllowRights,
                            DenyRights: existing.DenyRights | rule.FileSystemRights,
                            IsAccountOwner: existing.IsAccountOwner);
                    else
                        perSidGrants[path] = new PathAclAccumulator(
                            IsDirectory: isDirectory,
                            HasAllow: true,
                            HasDeny: existing.HasDeny,
                            AllowRights: existing.AllowRights | rule.FileSystemRights,
                            DenyRights: existing.DenyRights,
                            IsAccountOwner: isOwner || existing.IsAccountOwner);
                }
            }
        }

        var results = new Dictionary<string, AccountScanResult>(StringComparer.OrdinalIgnoreCase);

        foreach (var sid in knownSids)
        {
            var perSidGrants = grantRights[sid];
            var grants = new List<DiscoveredGrant>();

            foreach (var (path, acc) in perSidGrants)
            {
                var specialMask = acc.IsDirectory ? GrantRightsMapper.SpecialFolderMask : GrantRightsMapper.SpecialFileMask;

                if (acc.HasAllow)
                {
                    grants.Add(new DiscoveredGrant(
                        Path: path,
                        IsDeny: false,
                        Execute: (acc.AllowRights & GrantRightsMapper.ExecuteMask) != 0,
                        Write: (acc.AllowRights & (GrantRightsMapper.WriteFolderMask | GrantRightsMapper.WriteFileMask)) != 0,
                        Read: (acc.AllowRights & GrantRightsMapper.ReadMask) != 0,
                        Special: (acc.AllowRights & specialMask) != 0,
                        IsOwner: acc.IsAccountOwner));
                }

                if (acc.HasDeny)
                {
                    grants.Add(new DiscoveredGrant(
                        Path: path,
                        IsDeny: true,
                        Execute: (acc.DenyRights & GrantRightsMapper.ExecuteMask) != 0,
                        Write: (acc.DenyRights & (GrantRightsMapper.WriteFolderMask | GrantRightsMapper.WriteFileMask)) != 0,
                        Read: (acc.DenyRights & GrantRightsMapper.ReadMask) != 0,
                        Special: (acc.DenyRights & specialMask) != 0,
                        IsOwner: false));
                }
            }

            // Traverse paths that are not already classified as non-traverse allow grants.
            // Deny-only entries on the same path must not hide traverse-only grants.
            var traversePaths = traverseSeen[sid]
                .Where(p =>
                {
                    if (!perSidGrants.TryGetValue(p, out var acc))
                        return true;
                    return !acc.HasAllow;
                })
                .ToList();

            if (grants.Count > 0 || traversePaths.Count > 0)
                results[sid] = new AccountScanResult(grants, traversePaths);
        }

        return results;
    }

    private void ValidateRootAccessibility(string rootPath)
    {
        if (!aclAccessor.PathExists(rootPath, out bool isFolder) || !isFolder)
            return;
        try
        {
            _ = aclAccessor.GetSecurity(rootPath);
        }
        catch (Exception ex)
        {
            throw new IOException($"Protected root ACL could not be read: '{rootPath}'.", ex);
        }
    }
}
