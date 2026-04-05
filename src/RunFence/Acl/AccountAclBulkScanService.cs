using System.Security.AccessControl;
using System.Security.Principal;

namespace RunFence.Acl;

/// <summary>
/// Scans a folder tree for explicit NTFS ACEs belonging to any known SID,
/// building per-account results for bulk import into the ACL Manager.
/// </summary>
public class AccountAclBulkScanService(IFileSystemAclTraverser traverser) : IAccountAclBulkScanService
{
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
        var adminsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);

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
        foreach (var (path, _, security) in traverser.Traverse([rootPath], progress, ct))
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
                bool isTraverseOnly = !isDeny && (rule.FileSystemRights & ~AclScanConstants.TraverseOnlyMask) == 0;

                if (isTraverseOnly)
                {
                    traverseSeen[ruleSidValue].Add(path);
                }
                else
                {
                    var perSidGrants = grantRights[ruleSidValue];
                    perSidGrants.TryGetValue(path, out var existing);
                    bool isOwner = string.Equals(ownerSid, ruleSidValue, StringComparison.OrdinalIgnoreCase);
                    bool isAdminOwner = ownerSid != null && string.Equals(ownerSid, adminsSid.Value, StringComparison.OrdinalIgnoreCase);
                    if (isDeny)
                        perSidGrants[path] = new PathAclAccumulator(existing.HasAllow, true, existing.AllowRights, existing.DenyRights | rule.FileSystemRights,
                            isOwner || existing.IsAccountOwner, isAdminOwner || existing.IsAdminOwner);
                    else
                        perSidGrants[path] = new PathAclAccumulator(true, existing.HasDeny, existing.AllowRights | rule.FileSystemRights, existing.DenyRights,
                            isOwner || existing.IsAccountOwner, isAdminOwner || existing.IsAdminOwner);
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
                if (acc.HasAllow)
                {
                    grants.Add(new DiscoveredGrant(
                        Path: path,
                        IsDeny: false,
                        Execute: (acc.AllowRights & GrantedPathAclService.ExecuteRightsMask) != 0,
                        Write: (acc.AllowRights & GrantedPathAclService.WriteRightsMask) != 0,
                        Read: (acc.AllowRights & GrantedPathAclService.ReadRightsMask) != 0,
                        Special: (acc.AllowRights & GrantedPathAclService.SpecialRightsMask) != 0,
                        IsOwner: acc.IsAccountOwner));
                }

                if (acc.HasDeny)
                {
                    grants.Add(new DiscoveredGrant(
                        Path: path,
                        IsDeny: true,
                        Execute: (acc.DenyRights & GrantedPathAclService.ExecuteRightsMask) != 0,
                        Write: (acc.DenyRights & GrantedPathAclService.WriteRightsMask) != 0,
                        Read: (acc.DenyRights & GrantedPathAclService.ReadRightsMask) != 0,
                        Special: (acc.DenyRights & GrantedPathAclService.SpecialRightsMask) != 0,
                        IsOwner: acc.IsAccountOwner));
                }
            }

            // Traverse paths that are not already classified as full grants
            var traversePaths = traverseSeen[sid]
                .Where(p => !perSidGrants.ContainsKey(p))
                .ToList();

            if (grants.Count > 0 || traversePaths.Count > 0)
                results[sid] = new AccountScanResult(grants, traversePaths);
        }

        return results;
    }
}