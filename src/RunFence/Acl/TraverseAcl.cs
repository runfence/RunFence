using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Acl.Traverse;

namespace RunFence.Acl;

public interface ITraverseAcl
{
    void AddAllowAce(string path, SecurityIdentifier sid);
    bool HasExplicitTraverseAce(string dirPath, SecurityIdentifier sid);
    bool HasExplicitTraverseAceOrThrow(string dirPath, SecurityIdentifier sid);
    void RemoveTraverseOnlyAce(string path, SecurityIdentifier sid);
}

/// <summary>
/// Low-level ACL operations for container traverse ACEs. Uses non-propagating DACL writes so
/// directory tree inheritance recalculation is avoided on large trees.
/// </summary>
public class TraverseAcl(IPathSecurityDescriptorAccessor aclAccessor) : ITraverseAcl
{
    /// <summary>
    /// Adds a non-inheritable Allow ACE for the given SID with traverse rights
    /// (Traverse | ReadAttributes | Synchronize).
    /// </summary>
    public void AddAllowAce(string path, SecurityIdentifier sid)
    {
        if (!Directory.Exists(path))
            return;

        var security = aclAccessor.GetSecurity(path);
        security.AddAccessRule(new FileSystemAccessRule(
            sid,
            TraverseRightsHelper.TraverseRights,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow));
        aclAccessor.ApplyNonPropagatingAcl(path, security);
    }

    /// <summary>
    /// Returns true if <paramref name="dirPath"/> exists and has an explicit non-inheritable Allow ACE
    /// for <paramref name="sid"/> that grants exactly <see cref="TraverseRightsHelper.TraverseRights"/>.
    /// Returns false on any error.
    /// </summary>
    public bool HasExplicitTraverseAce(string dirPath, SecurityIdentifier sid)
        => HasExplicitTraverseAceCore(dirPath, sid, swallowErrors: true);

    /// <summary>
    /// Returns true if <paramref name="dirPath"/> exists and has an explicit non-inheritable Allow ACE
    /// for <paramref name="sid"/> that grants exactly <see cref="TraverseRightsHelper.TraverseRights"/>.
    /// Unlike <see cref="HasExplicitTraverseAce"/>, unexpected ACL read failures are propagated.
    /// </summary>
    public bool HasExplicitTraverseAceOrThrow(string dirPath, SecurityIdentifier sid)
        => HasExplicitTraverseAceCore(dirPath, sid, swallowErrors: false);

    private bool HasExplicitTraverseAceCore(string dirPath, SecurityIdentifier sid, bool swallowErrors)
    {
        if (!Directory.Exists(dirPath))
            return false;

        try
        {
            var security = aclAccessor.GetSecurity(dirPath);
            var rules = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier));
            return rules.Cast<FileSystemAccessRule>().Any(rule =>
                rule.AccessControlType == AccessControlType.Allow &&
                rule.IdentityReference.Equals(sid) &&
                rule.FileSystemRights == TraverseRightsHelper.TraverseRights &&
                rule.InheritanceFlags == InheritanceFlags.None);
        }
        catch
        {
            if (!swallowErrors)
                throw;

            return false;
        }
    }

    /// <summary>
    /// Removes only explicit Allow ACEs for <paramref name="sid"/> on <paramref name="path"/>
    /// where the ACE grants exactly <see cref="TraverseRightsHelper.TraverseRights"/> (no more, no less)
    /// and has no inheritance flags. ACEs with broader rights (e.g. ReadAndExecute) are left intact.
    /// </summary>
    public void RemoveTraverseOnlyAce(string path, SecurityIdentifier sid)
    {
        if (!Directory.Exists(path))
            return;

        var security = aclAccessor.GetSecurity(path);
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier));

        bool changed = false;
        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.AccessControlType == AccessControlType.Allow &&
                rule.IdentityReference is SecurityIdentifier ruleSid &&
                ruleSid.Equals(sid) &&
                rule.FileSystemRights == TraverseRightsHelper.TraverseRights &&
                rule.InheritanceFlags == InheritanceFlags.None)
            {
                security.RemoveAccessRuleSpecific(rule);
                changed = true;
            }
        }

        if (!changed)
            return;

        aclAccessor.ApplyNonPropagatingAcl(path, security);
    }
}
