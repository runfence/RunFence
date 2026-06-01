using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Core;

namespace RunFence.Acl.Permissions;

public class AdminRestrictionAclWriter(IPathSecurityDescriptorAccessor aclAccessor)
{
    public void RestrictToAdmins(string path)
    {
        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var adminsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);

        aclAccessor.ModifyAclWithFallback(path, security =>
        {
            security.SetAccessRuleProtection(true, false);

            var existingRules = security.GetAccessRules(true, false, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .ToList();
            foreach (var rule in existingRules)
                security.RemoveAccessRuleSpecific(rule);

            security.AddAccessRule(new FileSystemAccessRule(systemSid, FileSystemRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(adminsSid, FileSystemRights.FullControl, AccessControlType.Allow));
            AdminOperationMockAccessHelper.AddCurrentProcessFileSystemAccess(security, FileSystemRights.FullControl);
            return true;
        });
    }
}
