using System.Security.AccessControl;
using RunFence.Core;

namespace RunFence.Acl.Permissions;

public class GrantableAncestorPolicy(
    AclGroupSidResolver groupSidResolver,
    IPathSecurityDescriptorAccessor aclAccessor,
    IAclAccessEvaluator accessEvaluator)
{
    public bool NeedsPermissionGrant(string accountSid, string path, FileSystemRights rights)
        => NeedsPermissionGrant(accountSid, path, rights, unelevated: false);

    public bool NeedsPermissionGrantOrParent(string accountSid, string path, FileSystemRights rights)
    {
        var directory = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory) || PathHelper.IsBlockedAclPath(directory))
            return false;

        return NeedsPermissionGrant(accountSid, directory, rights);
    }

    public IReadOnlyList<string> GetGrantableAncestors(string accountSid, string path, FileSystemRights rights)
    {
        var start = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        var result = new List<string>();
        var current = start;

        while (!string.IsNullOrEmpty(current))
        {
            if (PathHelper.IsBlockedAclRoot(current))
                break;

            if (Directory.GetParent(current) == null)
                break;

            result.Add(current);
            current = Path.GetDirectoryName(current);
        }

        return result;
    }

    public bool NeedsPermissionGrant(string accountSid, string path, FileSystemRights rights, bool unelevated)
    {
        try
        {
            var fileSecurity = aclAccessor.GetSecurity(path);
            IReadOnlyList<string> groupSids = groupSidResolver.ResolveAccountGroupSids(accountSid);
            if (unelevated)
                groupSids = AclComputeHelper.ExcludeAdministratorsGroup(groupSids);

            var evaluation = accessEvaluator.Evaluate(fileSecurity, accountSid, groupSids, rights);
            if (evaluation.Status == AclAccessEvaluationStatus.Failed)
                return true;

            return (evaluation.GrantedRights & rights) != rights ||
                   (evaluation.DeniedRights & rights) != 0;
        }
        catch
        {
            return true;
        }
    }
}
