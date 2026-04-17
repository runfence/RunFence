using RunFence.Acl;
using RunFence.Acl.QuickAccess;
using RunFence.Acl.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;

namespace RunFence.RunAs;

/// <summary>
/// Applies permission grants for the RunAs flow: grants filesystem access for container or
/// credential SIDs and saves config when the database is modified.
/// </summary>
public class RunAsPermissionApplier(
    IPathGrantService pathGrantService,
    IDatabaseService databaseService,
    SessionContext session,
    IAppStateProvider appState,
    ILoggingService log,
    IQuickAccessPinService quickAccessPinService)
{
    /// <summary>
    /// Applies the permission grant for an AppContainer SID.
    /// Does nothing if <paramref name="containerSid"/> is empty (container SID not yet resolved).
    /// Saves config if the database was modified. EnsureAccess failures are caught and logged;
    /// SaveConfig failures propagate so the caller can present an error to the user.
    /// </summary>
    public void ApplyContainerGrant(AncestorPermissionResult grant, string? containerSid)
    {
        if (string.IsNullOrEmpty(containerSid))
            return;

        bool databaseModified;
        try
        {
            var result = pathGrantService.EnsureAccess(
                containerSid, grant.Path,
                grant.Rights,
                confirm: null);
            databaseModified = result.DatabaseModified;
        }
        catch (Exception ex)
        {
            log.Error("Failed to grant container permissions", ex);
            return;
        }

        if (databaseModified)
        {
            using var scope = session.PinDerivedKey.Unprotect();
            databaseService.SaveConfig(appState.Database, scope.Data,
                session.CredentialStore.ArgonSalt);
        }
    }

    /// <summary>
    /// Applies the permission grant for a credential (user account) SID.
    /// Saves config if the database was modified. Pins the granted folder to quick access
    /// if a new grant was added.
    /// </summary>
    public void ApplyCredentialGrant(AncestorPermissionResult grant, string credentialSid)
    {
        try
        {
            var result = pathGrantService.EnsureAccess(
                credentialSid, grant.Path,
                grant.Rights,
                confirm: null);

            if (result.DatabaseModified)
            {
                using var scope = session.PinDerivedKey.Unprotect();
                databaseService.SaveConfig(appState.Database, scope.Data, session.CredentialStore.ArgonSalt);
            }

            if (result.GrantAdded)
                quickAccessPinService.PinFolders(credentialSid, [grant.Path]);
        }
        catch (Exception ex)
        {
            log.Error("Failed to grant permissions", ex);
        }
    }
}
