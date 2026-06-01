using System.Security.AccessControl;
using System.Security.Principal;

namespace RunFence.Acl;

public class SpecificContainerAceConflictDetector(IPathSecurityDescriptorAccessor aclAccessor) : ISpecificContainerAceConflictDetector
{
    public bool HasExplicitSpecificContainerAce(string path)
    {
        if (!aclAccessor.PathExists(path, out _))
            return false;

        var security = aclAccessor.GetSecurity(path);
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier));

        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.IdentityReference is SecurityIdentifier sid &&
                AclHelper.IsSpecificContainerSid(sid.Value))
            {
                return true;
            }
        }

        return false;
    }

    public bool HasLowIntegrityAce(string path)
    {
        if (!aclAccessor.PathExists(path, out _))
            return false;

        var security = aclAccessor.GetSecurity(path);
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier));

        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.IdentityReference is SecurityIdentifier sid &&
                AclHelper.IsLowIntegritySid(sid.Value))
            {
                return true;
            }
        }

        return false;
    }
}
