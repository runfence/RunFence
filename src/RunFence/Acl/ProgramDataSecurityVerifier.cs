using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using RunFence.Acl.Traverse;

namespace RunFence.Acl;

public class ProgramDataSecurityVerifier(
    IHandleSecurityDescriptorAccessor handleSecurityDescriptorAccessor,
    ProgramDataOwnerPolicyService ownerPolicyService,
    ProgramDataAclProfilePolicy aclProfilePolicy)
{
    public void VerifyDirectorySecurity(
        SafeFileHandle handle,
        ProgramDataDirectoryAclProfile profile,
        ProgramDataAllowedOwnerPolicy ownerPolicy)
    {
        var security = handleSecurityDescriptorAccessor.GetSecurity(handle, isDirectory: true);
        var owner = GetOwnerSid(security);
        if (!ownerPolicyService.IsAllowedOwner(owner, ownerPolicy))
        {
            throw new InvalidOperationException($"Managed ProgramData directory owner '{owner.Value}' is not allowed.");
        }

        if (HasUnexpectedDirectoryRules(security, profile))
        {
            throw new InvalidOperationException("Managed ProgramData directory DACL verification failed.");
        }
    }

    public void VerifyFileSecurity(
        SafeFileHandle handle,
        ProgramDataFileAclProfile profile,
        ProgramDataAllowedOwnerPolicy ownerPolicy)
    {
        var security = handleSecurityDescriptorAccessor.GetSecurity(handle, isDirectory: false);
        var owner = GetOwnerSid(security);
        if (!ownerPolicyService.IsAllowedOwner(owner, ownerPolicy))
        {
            throw new InvalidOperationException($"Managed ProgramData file owner '{owner.Value}' is not allowed.");
        }

        if (HasUnexpectedFileRules(security, profile))
        {
            throw new InvalidOperationException("Managed ProgramData file DACL verification failed.");
        }
    }

    public bool HasExactTraverseAce(FileSystemSecurity security, SecurityIdentifier sid)
        => ProgramDataAclRuleHelper.GetExplicitRules(security).Any(rule =>
            rule.AccessControlType == AccessControlType.Allow &&
            rule.IdentityReference is SecurityIdentifier ruleSid &&
            ruleSid.Equals(sid) &&
            rule.FileSystemRights == TraverseRightsHelper.TraverseRights &&
            rule.InheritanceFlags == InheritanceFlags.None &&
            rule.PropagationFlags == PropagationFlags.None);

    public bool HasUntrustedFileWriteOrOwnerAccess(FileSystemSecurity security, ProgramDataFileAclProfile profile)
    {
        var expectedRules = aclProfilePolicy.GetExpectedFileRules(profile);
        foreach (var rule in ProgramDataAclRuleHelper.GetAllRules(security))
        {
            if (rule.AccessControlType != AccessControlType.Allow)
            {
                continue;
            }

            if (expectedRules.Any(expectedRule => expectedRule.Matches(rule)))
            {
                continue;
            }

            if (aclProfilePolicy.HasDangerousFileRights(rule.FileSystemRights))
            {
                return true;
            }
        }

        return false;
    }

    private bool HasUnexpectedDirectoryRules(FileSystemSecurity security, ProgramDataDirectoryAclProfile profile)
    {
        if (!security.AreAccessRulesProtected)
        {
            return true;
        }

        var expectedRules = aclProfilePolicy.GetExpectedDirectoryRules(profile);
        foreach (var rule in ProgramDataAclRuleHelper.GetExplicitRules(security))
        {
            if (expectedRules.Any(expectedRule => expectedRule.Matches(rule)))
            {
                continue;
            }

            if (ProgramDataAclRuleHelper.IsOwnerRelativeRule(rule) || ProgramDataAclRuleHelper.IsExactTraverseAce(rule))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private bool HasUnexpectedFileRules(FileSystemSecurity security, ProgramDataFileAclProfile profile)
    {
        if (!security.AreAccessRulesProtected)
        {
            return true;
        }

        var expectedRules = aclProfilePolicy.GetExpectedFileRules(profile);
        foreach (var rule in ProgramDataAclRuleHelper.GetExplicitRules(security))
        {
            if (expectedRules.Any(expectedRule => expectedRule.Matches(rule)))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static SecurityIdentifier GetOwnerSid(FileSystemSecurity security)
        => (SecurityIdentifier?)security.GetOwner(typeof(SecurityIdentifier))
           ?? throw new InvalidOperationException("Managed ProgramData object does not have a SID owner.");
}
