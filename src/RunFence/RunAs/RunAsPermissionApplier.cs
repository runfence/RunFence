using RunFence.Acl;
using RunFence.Acl.QuickAccess;
using RunFence.Acl.UI;
using RunFence.Core.Models;

namespace RunFence.RunAs;

/// <summary>
/// Applies permission grants for the RunAs flow: grants filesystem access for container or
/// credential SIDs and optionally pins the granted folder for credential launches.
/// </summary>
public class RunAsPermissionApplier(
    IGrantMutatorService grantMutatorService,
    IQuickAccessPinService quickAccessPinService)
{
    public GrantApplyResult ApplyContainerGrant(AncestorPermissionResult grant, string? containerSid)
    {
        if (string.IsNullOrEmpty(containerSid))
            return default;

        return grantMutatorService.EnsureAccess(
            containerSid,
            grant.Path,
            grant.Rights,
            confirm: null);
    }

    /// <summary>
    /// Applies the permission grant for a credential (user account) SID.
    /// Pins the granted folder to quick access if a new durable grant was added.
    /// </summary>
    public GrantApplyResult ApplyCredentialGrant(AncestorPermissionResult grant, string credentialSid)
    {
        var result = grantMutatorService.EnsureAccess(
            credentialSid,
            grant.Path,
            grant.Rights,
            confirm: null);

        if (result.GrantApplied)
            quickAccessPinService.PinFolders(credentialSid, [grant.Path]);

        return result;
    }
}
