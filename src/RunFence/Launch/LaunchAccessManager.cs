using System.Security.AccessControl;
using RunFence.Acl;
using RunFence.Core.Models;

namespace RunFence.Launch;

public sealed class LaunchAccessManager(IGrantMutatorService grantMutatorService) : ILaunchAccessManager
{
    public GrantApplyResult EnsureAccess(LaunchIdentity identity, string path,
        FileSystemRights rights, Func<string, string, bool>? confirm)
    {
        var effectiveUnelevated = identity.IsUnelevated ?? true;
        var result = grantMutatorService.EnsureAccess(identity.Sid, path, rights, confirm, effectiveUnelevated);
        if (identity is not AccountLaunchIdentity { PrivilegeLevel: PrivilegeLevel.LowIntegrity })
            return result;

        var lowIntegrityResult = grantMutatorService.EnsureAccess(
            AclHelper.LowIntegritySid,
            path,
            rights,
            confirm: null,
            effectiveUnelevated);

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

    public GrantApplyResult EnsureAccess(string sid, string path, FileSystemRights rights,
        Func<string, string, bool>? confirm, bool unelevated)
        => grantMutatorService.EnsureAccess(sid, path, rights, confirm, unelevated);
}
