using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Core.Models;

namespace RunFence.Acl;

/// <summary>
/// Low-level ACL mutation and path-normalization helpers shared by
/// <see cref="AclDenyModeService"/> and <see cref="AclAllowModeService"/>.
/// </summary>
public static class AclHelper
{
    public static HashSet<SecurityIdentifier> BuildLocalUserSidSet(IEnumerable<LocalUserAccount> users)
    {
        var result = new HashSet<SecurityIdentifier>();
        foreach (var user in users)
        {
            try
            {
                result.Add(new SecurityIdentifier(user.Sid));
            }
            catch (ArgumentException)
            {
            }
        }

        return result;
    }

    public static string NormalizePath(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);

    public static bool PathIsAtOrBelow(string candidatePath, string ancestorPath)
    {
        var normCandidate = NormalizePath(candidatePath);
        var normAncestor = NormalizePath(ancestorPath);
        if (normCandidate.Equals(normAncestor, StringComparison.OrdinalIgnoreCase))
            return true;
        return normCandidate.StartsWith(
            normAncestor + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Diff-based idempotent ACL mutation. Compares desired managed ACEs against
    /// current explicit ACEs, writes only if changes are needed.
    /// </summary>
    /// <param name="security">The security descriptor to modify in-place.</param>
    /// <param name="desiredRules">The complete set of managed ACEs that should exist.</param>
    /// <param name="isManagedAce">Predicate to identify existing ACEs that are "managed" by us.</param>
    /// <returns>True if changes were made (caller should write back), false if already correct.</returns>
    public static bool ApplyAclDiff(
        FileSystemSecurity security,
        IReadOnlyList<FileSystemAccessRule> desiredRules,
        Func<FileSystemAccessRule, bool> isManagedAce)
    {
        var existingRules = security.GetAccessRules(true, false, typeof(SecurityIdentifier));
        var existingManaged = existingRules.Cast<FileSystemAccessRule>().Where(rule => isManagedAce(rule)).ToList();

        // Compare: match on SID + rights + InheritanceFlags + PropagationFlags + AccessControlType
        var desiredSet = new HashSet<(string sid, FileSystemRights rights, InheritanceFlags inh, PropagationFlags prop, AccessControlType type)>();
        foreach (var rule in desiredRules)
        {
            desiredSet.Add((
                ((SecurityIdentifier)rule.IdentityReference).Value,
                rule.FileSystemRights,
                rule.InheritanceFlags,
                rule.PropagationFlags,
                rule.AccessControlType));
        }

        var existingSet = new HashSet<(string sid, FileSystemRights rights, InheritanceFlags inh, PropagationFlags prop, AccessControlType type)>();
        foreach (var rule in existingManaged)
        {
            existingSet.Add((
                ((SecurityIdentifier)rule.IdentityReference).Value,
                rule.FileSystemRights,
                rule.InheritanceFlags,
                rule.PropagationFlags,
                rule.AccessControlType));
        }

        if (desiredSet.SetEquals(existingSet))
            return false;

        // Remove stale managed ACEs
        foreach (var rule in existingManaged)
        {
            security.RemoveAccessRuleSpecific(rule);
        }

        // Add desired ACEs
        foreach (var rule in desiredRules)
        {
            security.AddAccessRule(rule);
        }

        return true;
    }

    public static InheritanceFlags InheritanceFlagsFor(bool isFolder)
        => isFolder ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit : InheritanceFlags.None;

    public static void ModifyAcl(string path, bool isFolder, Action<FileSystemSecurity> modify)
        => ModifyAclIf(path, isFolder, security =>
        {
            modify(security);
            return true;
        });

    public static bool ModifyAclIf(string path, bool isFolder, Func<FileSystemSecurity, bool> modify)
    {
        if (isFolder)
        {
            var dirInfo = new DirectoryInfo(path);
            var security = dirInfo.GetAccessControl();
            if (!modify(security))
                return false;
            dirInfo.SetAccessControl(security);
        }
        else
        {
            var fileInfo = new FileInfo(path);
            var security = fileInfo.GetAccessControl();
            if (!modify(security))
                return false;
            fileInfo.SetAccessControl(security);
        }

        return true;
    }
}