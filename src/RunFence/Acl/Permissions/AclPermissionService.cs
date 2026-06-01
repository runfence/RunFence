using System.Security.AccessControl;

namespace RunFence.Acl.Permissions;

/// <summary>
/// Injectable service for ACL permission checks and write operations: restricting access
/// and querying effective permissions.
/// </summary>
public class AclPermissionService(
    AclGroupSidResolver groupSidResolver,
    GrantableAncestorPolicy ancestorPolicy,
    AdminRestrictionAclWriter adminRestrictionAclWriter,
    IAclAccessEvaluator accessEvaluator) : IAclPermissionService
{
    public bool HasEffectiveRights(
        FileSystemSecurity security,
        string accountSid,
        IReadOnlyList<string> accountGroupSids,
        FileSystemRights requiredRights)
    {
        var evaluation = accessEvaluator.Evaluate(security, accountSid, accountGroupSids, requiredRights);
        if (evaluation.Status == AclAccessEvaluationStatus.Failed)
            return false;

        return (evaluation.GrantedRights & requiredRights) == requiredRights &&
               (evaluation.DeniedRights & requiredRights) == 0;
    }

    public List<string> ResolveAccountGroupSids(string accountSid)
        => groupSidResolver.ResolveAccountGroupSids(accountSid);

    public bool NeedsPermissionGrant(
        string filePath,
        string accountSid,
        FileSystemRights requiredRights = FileSystemRights.ReadAndExecute,
        bool unelevated = false)
        => ancestorPolicy.NeedsPermissionGrant(accountSid, filePath, requiredRights, unelevated);

    public bool NeedsPermissionGrantOrParent(string filePath, string accountSid)
        => ancestorPolicy.NeedsPermissionGrantOrParent(accountSid, filePath, FileSystemRights.ReadAndExecute);

    public IReadOnlyList<string> GetGrantableAncestors(string filePath)
        => ancestorPolicy.GetGrantableAncestors(string.Empty, filePath, FileSystemRights.ReadAndExecute);

    public void RestrictToAdmins(string filePath)
    {
        adminRestrictionAclWriter.RestrictToAdmins(filePath);
    }
}
