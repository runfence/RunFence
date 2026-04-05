using System.Security.Principal;
using RunFence.Acl.Permissions;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Acl.Traverse;

/// <summary>
/// Grants and reverts non-inheritable Traverse + ReadAttributes + Synchronize ACEs on ancestor
/// directories for a given SID. Shared by both AppContainer and regular-user traverse logic.
/// Uses SetFileSecurity (via TraverseAclNative) to avoid recursive NTFS auto-inheritance propagation
/// that SetNamedSecurityInfo triggers on large directory trees.
/// </summary>
public class AncestorTraverseGranter(ILoggingService log, IAclPermissionService aclPermission)
{
    /// <summary>
    /// Grants Traverse + ReadAttributes + Synchronize (no inheritance) on <paramref name="path"/> and each
    /// ancestor up to the drive root. When <paramref name="aclPermission"/> and
    /// <paramref name="groupSids"/> are provided, skips directories where <paramref name="identity"/>
    /// already has effective traverse access (explicit + inherited + group membership), but still records
    /// them for cleanup tracking. An existing ACE with different rights (e.g. ReadData) does not count
    /// as covered — the effective rights check is authoritative. Without a permission service, falls back
    /// to the explicit-ACE check as a proxy.
    /// Returns the visited directory list and whether any new ACEs were actually added.
    /// The list is stored in <see cref="GrantedPathEntry.AllAppliedPaths"/> for reliable cleanup.
    /// </summary>
    public (List<string> AppliedPaths, bool AnyAceAdded) GrantOnPathAndAncestors(
        string path, SecurityIdentifier identity,
        IReadOnlyList<string>? groupSids = null)
    {
        var current = new DirectoryInfo(path);
        var visitedPaths = new List<string>();
        bool anyAceAdded = false;

        while (current != null)
        {
            var next = current.Parent;
            try
            {
                if (!current.Exists)
                {
                    current = next;
                    continue;
                }

                // Check effective traverse access (explicit + inherited + group membership).
                // Runs unconditionally when aclPermission is available — an existing ACE may grant
                // different rights (e.g. ReadData instead of Traverse) and must not be treated as covered.
                // When no permission service is available, fall back to the explicit-ACE check.
                bool effectivelyCovered;
                if (groupSids != null)
                {
                    try
                    {
                        var dirSecurity = new DirectoryInfo(current.FullName).GetAccessControl();
                        effectivelyCovered = aclPermission.HasEffectiveRights(dirSecurity, identity.Value, groupSids, TraverseRightsHelper.TraverseRights);
                    }
                    catch (Exception ex)
                    {
                        log.Warn($"Traverse: effective rights check failed for '{current.FullName}': {ex.Message} — adding ACE");
                        effectivelyCovered = false;
                    }
                }
                else
                {
                    effectivelyCovered = TraverseAclNative.HasExplicitTraverseAce(current.FullName, identity);
                }

                visitedPaths.Add(current.FullName);

                if (effectivelyCovered)
                {
                    log.Info($"Traverse: '{current.FullName}' already has effective traverse — tracking, skipping ACE");
                    current = next;
                    continue;
                }

                TraverseAclNative.AddAllowAce(current.FullName, identity);
                anyAceAdded = true;
                log.Info($"Granted traverse on '{current.FullName}'");
            }
            catch (Exception ex)
            {
                log.Warn($"Failed to grant traverse on '{current!.FullName}': {ex.Message}");
            }

            current = next;
        }

        return (visitedPaths, anyAceAdded);
    }

    /// <summary>
    /// Removes only explicit traverse-only Allow ACEs (exactly Traverse | ReadAttributes | Synchronize, no inheritance)
    /// for <paramref name="identity"/> from <paramref name="dirPath"/>. Leaves broader ACEs
    /// (e.g. ReadAndExecute from a parent grant) intact.
    /// </summary>
    public void RemoveAce(string dirPath, SecurityIdentifier identity)
    {
        try
        {
            if (TraverseAclNative.HasExplicitTraverseAce(dirPath, identity))
            {
                TraverseAclNative.RemoveTraverseOnlyAce(dirPath, identity);
                log.Info($"Removed traverse ACE on '{dirPath}'");
            }
        }
        catch (Exception ex)
        {
            log.Warn($"Failed to remove traverse ACE on '{dirPath}': {ex.Message}");
        }
    }

    /// <summary>
    /// Reverts traverse ACEs for <paramref name="removingEntry"/> while preserving those still needed
    /// by <paramref name="remainingEntries"/> or by <paramref name="additionalStillNeeded"/>.
    /// Collects paths to keep from remaining entries and the additional set, then removes ACEs from
    /// paths in the removing entry that are no longer needed.
    /// </summary>
    public void RevertForPath(
        SecurityIdentifier identity,
        GrantedPathEntry removingEntry,
        IEnumerable<GrantedPathEntry> remainingEntries,
        HashSet<string>? additionalStillNeeded = null)
    {
        var stillNeeded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in remainingEntries)
            TraversePathsHelper.CollectPaths(entry, stillNeeded);

        if (additionalStillNeeded != null)
            stillNeeded.UnionWith(additionalStillNeeded);

        var toRemove = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        TraversePathsHelper.CollectPaths(removingEntry, toRemove);

        foreach (var dir in toRemove)
        {
            if (!stillNeeded.Contains(dir))
                RemoveAce(dir, identity);
        }
    }
}