using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Acl.Traverse;

namespace RunFence.Acl;

public class ProgramDataDirectoryAclBuilder(ProgramDataAclProfilePolicy aclProfilePolicy)
{
    public DirectorySecurity BuildDirectorySecurity(
        ProgramDataDirectoryAclProfile aclProfile,
        FileSystemSecurity? existingSecurity,
        string? additionalTraverseSid = null)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        foreach (var rule in aclProfilePolicy.GetExpectedDirectoryRules(aclProfile))
        {
            ProgramDataAclRuleHelper.AddRuleDeduped(security, rule.CreateAccessRule());
        }

        if (!string.IsNullOrWhiteSpace(additionalTraverseSid))
        {
            ProgramDataAclRuleHelper.AddRuleDeduped(
                security,
                new FileSystemAccessRule(
                    new SecurityIdentifier(additionalTraverseSid),
                    TraverseRightsHelper.TraverseRights,
                    InheritanceFlags.None,
                    PropagationFlags.None,
                    AccessControlType.Allow));
        }

        if (existingSecurity != null)
        {
            foreach (var rule in ProgramDataAclRuleHelper.GetExplicitRules(existingSecurity))
            {
                if (ProgramDataAclRuleHelper.IsOwnerRelativeRule(rule) || ProgramDataAclRuleHelper.IsExactTraverseAce(rule))
                {
                    ProgramDataAclRuleHelper.AddRuleDeduped(security, rule);
                }
            }
        }

        return security;
    }

    public FileSecurity BuildFileSecurity(ProgramDataFileAclProfile aclProfile)
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        foreach (var rule in aclProfilePolicy.GetExpectedFileRules(aclProfile))
        {
            ProgramDataAclRuleHelper.AddRuleDeduped(security, rule.CreateAccessRule());
        }

        return security;
    }

    public DirectorySecurity BuildRestrictedDirectorySecurity(
        ProgramDataDirectoryAclProfile aclProfile,
        FileSystemSecurity? existingSecurity,
        IReadOnlyCollection<ProgramDataPrincipalAccess> additionalAccess)
    {
        var security = BuildDirectorySecurity(aclProfile, existingSecurity);
        foreach (var access in additionalAccess)
        {
            ProgramDataAclRuleHelper.AddRuleDeduped(
                security,
                new FileSystemAccessRule(
                    access.Principal,
                    access.Rights,
                    access.InheritanceFlags,
                    access.PropagationFlags,
                    AccessControlType.Allow));
        }

        return security;
    }

    public FileSecurity BuildRestrictedFileSecurity(
        ProgramDataFileAclProfile aclProfile,
        IReadOnlyCollection<ProgramDataPrincipalAccess> additionalAccess)
    {
        var security = BuildFileSecurity(aclProfile);
        foreach (var access in additionalAccess)
        {
            ProgramDataAclRuleHelper.AddRuleDeduped(
                security,
                new FileSystemAccessRule(
                    access.Principal,
                    access.Rights,
                    access.InheritanceFlags,
                    access.PropagationFlags,
                    AccessControlType.Allow));
        }

        return security;
    }
}
