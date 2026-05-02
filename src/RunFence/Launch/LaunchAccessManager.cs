using System.Security.AccessControl;
using RunFence.Acl;
using RunFence.Core.Models;

namespace RunFence.Launch;

public sealed class LaunchAccessManager(IGrantMutatorService grantMutatorService) : ILaunchAccessManager
{
    public GrantOperationResult EnsureAccess(LaunchIdentity identity, string path,
        FileSystemRights rights, Func<string, string, bool>? confirm, bool unelevated)
    {
        var result = grantMutatorService.EnsureAccess(identity.Sid, path, rights, confirm, unelevated);
        if (identity is AccountLaunchIdentity { PrivilegeLevel: PrivilegeLevel.LowIntegrity })
        {
            var liResult = grantMutatorService.EnsureAccess(
                AclHelper.LowIntegritySid, path, rights, confirm, unelevated);
            return new GrantOperationResult(
                result.GrantAdded || liResult.GrantAdded,
                result.TraverseAdded || liResult.TraverseAdded,
                result.DatabaseModified || liResult.DatabaseModified);
        }
        return result;
    }
}
