using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Acl;

/// <summary>
/// Discovered NTFS rights for a single grant path during an ACL scan.
/// Used to pre-populate <see cref="GrantedPathEntry.SavedRights"/> without a second read pass.
/// Ownership is read from the same <see cref="FileSystemSecurity"/> object as ACEs (no extra NTFS I/O).
/// </summary>
public record DiscoveredGrantRights(
    // Allow-mode rights
    bool AllowExecute,
    bool AllowWrite,
    bool AllowSpecial,
    // Deny-mode rights (Read + Execute are user-configurable; Write+Special are always-on)
    bool DenyRead,
    bool DenyExecute,
    // Ownership (used for the Own checkbox in SavedRightsState)
    bool IsAccountOwner, // true = the scanned SID is the owner (used for allow-mode Own)
    bool IsAdminOwner); // true = BUILTIN\Administrators is the owner (used for deny-mode Own)

/// <summary>
/// Result of an ACL Manager folder scan: explicit grant ACEs found inside the folder tree,
/// and traverse paths found inside the folder or on ancestor directories.
/// </summary>
public record ScanResult(
    List<(string Path, bool IsDeny)> GrantPaths,
    List<string> TraversePaths,
    /// <summary>
    /// Discovered rights per grant path (case-insensitive key). Used to pre-populate
    /// <see cref="GrantedPathEntry.SavedRights"/> when registering scan results.
    /// </summary>
    Dictionary<string, DiscoveredGrantRights> DiscoveredRights);

/// <summary>
/// Scans a folder tree for explicit Allow/Deny ACEs belonging to a specific SID,
/// returning paths that can be batch-registered as grants and traverse entries in the ACL Manager.
/// </summary>
public class AclManagerScanService(IFileSystemAclTraverser traverser, ILoggingService log) : IAclManagerScanService
{
    private static readonly SecurityIdentifier AdminsSid =
        new(WellKnownSidType.BuiltinAdministratorsSid, null);

    /// <summary>
    /// Walks <paramref name="rootPath"/> and collects:
    /// <list type="bullet">
    ///   <item><description>Grant paths: explicit Allow/Deny ACEs (non-traverse-only) for <paramref name="sid"/> inside the folder tree.</description></item>
    ///   <item><description>Traverse paths: traverse-only Allow ACEs inside the folder, plus ancestor directories of <paramref name="rootPath"/> that have any explicit Allow ACE for <paramref name="sid"/>.</description></item>
    /// </list>
    /// Paths already classified as grants are excluded from the traverse list.
    /// </summary>
    public async Task<ScanResult> ScanAsync(
        string rootPath,
        string sid,
        IProgress<long> progress,
        CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            var identity = new SecurityIdentifier(sid);

            // Accumulated rights per path (OR-merged across multiple ACEs for the same SID)
            var grantsRights = new Dictionary<string, PathAclAccumulator>(StringComparer.OrdinalIgnoreCase);
            var traverseSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Privileges (SeBackup/SeRestore/SeTakeOwnership) are enabled once at startup.
            // Scan inside the folder for explicit ACEs
            foreach (var (path, _, security) in traverser.Traverse([rootPath], progress, ct))
            {
                var rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier));
                foreach (FileSystemAccessRule rule in rules)
                {
                    if (!string.Equals(rule.IdentityReference.Value, sid, StringComparison.OrdinalIgnoreCase))
                        continue;

                    bool isDeny = rule.AccessControlType == AccessControlType.Deny;
                    bool isTraverseOnly = !isDeny && (rule.FileSystemRights & ~AclScanConstants.TraverseOnlyMask) == 0;

                    if (isTraverseOnly)
                    {
                        traverseSeen.Add(path);
                    }
                    else
                    {
                        grantsRights.TryGetValue(path, out var existing);

                        // Read owner once per path (on first non-traverse ACE encounter).
                        // FileSystemAclTraverser already fetches Owner + Access sections, so no extra I/O.
                        bool isAccountOwner, isAdminOwner;
                        if (existing.HasAllow || existing.HasDeny)
                        {
                            // Already recorded ownership for this path — reuse it.
                            isAccountOwner = existing.IsAccountOwner;
                            isAdminOwner = existing.IsAdminOwner;
                        }
                        else
                        {
                            var ownerSid = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
                            isAccountOwner = ownerSid != null && ownerSid.Equals(identity);
                            isAdminOwner = ownerSid != null && ownerSid.Equals(AdminsSid);
                        }

                        if (isDeny)
                            grantsRights[path] = new PathAclAccumulator(existing.HasAllow, true, existing.AllowRights, existing.DenyRights | rule.FileSystemRights, isAccountOwner,
                                isAdminOwner);
                        else
                            grantsRights[path] = new PathAclAccumulator(true, existing.HasDeny, existing.AllowRights | rule.FileSystemRights, existing.DenyRights, isAccountOwner,
                                isAdminOwner);
                    }
                }
            }

            // Scan ancestor directories of rootPath for explicit Allow ACEs
            var ancestor = new DirectoryInfo(rootPath).Parent;
            while (ancestor != null)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (ancestor.Exists)
                    {
                        var acl = ancestor.GetAccessControl(AccessControlSections.Access);
                        var rules = acl.GetAccessRules(true, false, typeof(SecurityIdentifier));
                        if (rules.Cast<FileSystemAccessRule>().Any(rule => rule.AccessControlType == AccessControlType.Allow &&
                                                                           string.Equals(rule.IdentityReference.Value, sid, StringComparison.OrdinalIgnoreCase)))
                        {
                            traverseSeen.Add(ancestor.FullName);
                        }
                    }
                }
                catch (Exception ex)
                {
                    log.Warn($"Scan: failed to read ACL on '{ancestor.FullName}': {ex.Message}");
                }

                ancestor = ancestor.Parent;
            }

            var grantPaths = new List<(string Path, bool IsDeny)>();
            var discoveredRights = new Dictionary<string, DiscoveredGrantRights>(StringComparer.OrdinalIgnoreCase);

            foreach (var (path, acc) in grantsRights)
            {
                if (acc.HasAllow)
                    grantPaths.Add((path, false));
                if (acc.HasDeny)
                    grantPaths.Add((path, true));

                discoveredRights[path] = new DiscoveredGrantRights(AllowExecute: (acc.AllowRights & GrantedPathAclService.ExecuteRightsMask) == GrantedPathAclService.ExecuteRightsMask,
                    AllowWrite: (acc.AllowRights & GrantedPathAclService.WriteRightsMask) == GrantedPathAclService.WriteRightsMask,
                    AllowSpecial: (acc.AllowRights & GrantedPathAclService.SpecialRightsMask) == GrantedPathAclService.SpecialRightsMask,
                    DenyRead: (acc.DenyRights & GrantedPathAclService.ReadRightsMask) == GrantedPathAclService.ReadRightsMask,
                    DenyExecute: (acc.DenyRights & GrantedPathAclService.ExecuteRightsMask) == GrantedPathAclService.ExecuteRightsMask,
                    IsAccountOwner: acc.IsAccountOwner,
                    IsAdminOwner: acc.IsAdminOwner);
            }

            // Exclude paths already classified as full grants from the traverse list
            var traversePaths = traverseSeen.Where(p => !grantsRights.ContainsKey(p)).ToList();

            return new ScanResult(grantPaths, traversePaths, discoveredRights);
        }, ct);
    }
}