using System.Security.AccessControl;
using Microsoft.Win32.SafeHandles;
using RunFence.Core;

namespace RunFence.Acl;

public sealed class ProgramDataExplicitAclApplier(
    ILoggingService log,
    IHandleSecurityDescriptorAccessor handleSecurityDescriptorAccessor)
{
    public void ApplyAcl(
        SafeFileHandle handle,
        string path,
        bool isDirectory,
        FileSystemSecurity targetSecurity)
    {
        string? changeSummary = null;
        if (handleSecurityDescriptorAccessor.ModifyAclWithFallback(handle, isDirectory, security =>
        {
            FileSystemSecurity before = security switch
            {
                DirectorySecurity => new DirectorySecurity(),
                FileSecurity => new FileSecurity(),
                _ => throw new InvalidOperationException(
                    $"Unsupported filesystem security type '{security.GetType().FullName}'.")
            };
            before.SetSecurityDescriptorBinaryForm(security.GetSecurityDescriptorBinaryForm());

            if (ProgramDataAclRuleHelper.GetSecuritySignature(before) ==
                ProgramDataAclRuleHelper.GetSecuritySignature(targetSecurity) &&
                security.AreAccessRulesCanonical)
            {
                return false;
            }

            security.SetSecurityDescriptorSddlForm(
                targetSecurity.GetSecurityDescriptorSddlForm(AccessControlSections.Access),
                AccessControlSections.Access);
            changeSummary = ProgramDataSecurityChangeFormatter.DescribeAccessChange(before, security);
            return true;
        }))
        {
            log.Info($"ProgramData security updated explicit object ACL on '{path}': {changeSummary}.");
        }
    }

    public void VerifyDacl(
        SafeFileHandle handle,
        bool isDirectory,
        FileSystemSecurity expectedSecurity)
    {
        var actualSecurity = handleSecurityDescriptorAccessor.GetSecurity(handle, isDirectory);
        if (ProgramDataAclRuleHelper.GetSecuritySignature(actualSecurity) !=
            ProgramDataAclRuleHelper.GetSecuritySignature(expectedSecurity))
        {
            throw new InvalidOperationException("Explicit ProgramData object DACL verification failed.");
        }
    }
}
