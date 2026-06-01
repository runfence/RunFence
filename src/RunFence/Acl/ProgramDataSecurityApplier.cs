using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using RunFence.Core;

namespace RunFence.Acl;

public class ProgramDataSecurityApplier(
    ILoggingService log,
    IHandleSecurityDescriptorAccessor handleSecurityDescriptorAccessor,
    ProgramDataDirectoryAclBuilder aclBuilder,
    ProgramDataPathPolicyCatalog pathPolicyCatalog)
{
    public void ApplyDirectoryAcl(
        SafeFileHandle handle,
        string path,
        ProgramDataDirectoryAclProfile profile,
        FileSystemSecurity? existingSecurity)
    {
        string? changeSummary = null;
        if (handleSecurityDescriptorAccessor.ModifyAclWithFallback(handle, isFolder: true, security =>
        {
            var before = CloneSecurity(security);
            var targetSecurity = aclBuilder.BuildDirectorySecurity(profile, existingSecurity ?? security);
            var changed = ApplyAccessRules(security, targetSecurity);
            if (changed)
            {
                changeSummary = ProgramDataSecurityChangeFormatter.DescribeAccessChange(before, security);
            }

            return changed;
        }))
        {
            log.Info($"ProgramData security updated directory ACL on '{path}': {changeSummary}.");
        }
    }

    public void ApplyFileAcl(SafeFileHandle handle, string path, ProgramDataFileAclProfile profile)
    {
        var targetSecurity = aclBuilder.BuildFileSecurity(profile);
        string? changeSummary = null;
        if (handleSecurityDescriptorAccessor.ModifyAclWithFallback(handle, isFolder: false, security =>
        {
            var before = CloneSecurity(security);
            var changed = ApplyAccessRules(security, targetSecurity);
            if (changed)
            {
                changeSummary = ProgramDataSecurityChangeFormatter.DescribeAccessChange(before, security);
            }

            return changed;
        }))
        {
            log.Info($"ProgramData security updated file ACL on '{path}': {changeSummary}.");
        }
    }

    public void ApplyTraverseOnlyAccess(
        SafeFileHandle handle,
        string path,
        ProgramDataDirectoryAclProfile profile,
        SecurityIdentifier targetSid)
    {
        var effectiveProfile = pathPolicyCatalog.RegisterDirectoryProfile(path, profile);
        string? changeSummary = null;
        if (handleSecurityDescriptorAccessor.ModifyAclWithFallback(handle, isFolder: true, security =>
        {
            var before = CloneSecurity(security);
            var targetSecurity = aclBuilder.BuildDirectorySecurity(effectiveProfile, security, targetSid.Value);
            var changed = ApplyAccessRules(security, targetSecurity);
            if (changed)
            {
                changeSummary = ProgramDataSecurityChangeFormatter.DescribeAccessChange(before, security);
            }

            return changed;
        }))
        {
            log.Info($"ProgramData security updated traverse-only ACL on '{path}' for '{targetSid.Value}': {changeSummary}.");
        }
    }

    private static bool ApplyAccessRules(FileSystemSecurity destination, FileSystemSecurity target)
    {
        var before = ProgramDataAclRuleHelper.GetSecuritySignature(destination);
        var targetSignature = ProgramDataAclRuleHelper.GetSecuritySignature(target);
        if (string.Equals(before, targetSignature, StringComparison.Ordinal) && destination.AreAccessRulesCanonical)
        {
            return false;
        }

        destination.SetSecurityDescriptorSddlForm(
            target.GetSecurityDescriptorSddlForm(AccessControlSections.Access),
            AccessControlSections.Access);
        return true;
    }

    private static FileSystemSecurity CloneSecurity(FileSystemSecurity security)
    {
        FileSystemSecurity clone = security switch
        {
            DirectorySecurity => new DirectorySecurity(),
            FileSecurity => new FileSecurity(),
            _ => throw new InvalidOperationException($"Unsupported filesystem security type '{security.GetType().FullName}'.")
        };
        clone.SetSecurityDescriptorBinaryForm(security.GetSecurityDescriptorBinaryForm());
        return clone;
    }
}
