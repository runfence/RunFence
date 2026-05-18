using System.Security.AccessControl;
using RunFence.Acl;
using RunFence.Core.Models;

namespace RunFence.Launch;

public sealed class LaunchAccessManager(IGrantMutatorService grantMutatorService) : ILaunchAccessManager
{
    public GrantApplyResult EnsureAccess(LaunchIdentity identity, string path,
        FileSystemRights rights, Func<string, string, bool>? confirm, bool unelevated)
    {
        var result = grantMutatorService.EnsureAccess(identity.Sid, path, rights, confirm, unelevated);
        if (identity is not AccountLaunchIdentity { PrivilegeLevel: PrivilegeLevel.LowIntegrity })
            return result;

        var lowIntegrityResult = grantMutatorService.EnsureAccess(
            AclHelper.LowIntegritySid,
            path,
            rights,
            confirm: null,
            unelevated);

        var databaseModified = result.DatabaseModified || lowIntegrityResult.DatabaseModified;
        var durableSaveCompleted =
            databaseModified &&
            (!result.DatabaseModified || result.DurableSaveCompleted) &&
            (!lowIntegrityResult.DatabaseModified || lowIntegrityResult.DurableSaveCompleted);

        return new GrantApplyResult(
            GrantApplied: result.GrantApplied || lowIntegrityResult.GrantApplied,
            TraverseApplied: result.TraverseApplied || lowIntegrityResult.TraverseApplied,
            DatabaseModified: databaseModified,
            DurableSaveCompleted: durableSaveCompleted,
            Warnings: result.Warnings.Concat(lowIntegrityResult.Warnings).ToList());
    }
}
