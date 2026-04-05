using System.Security.AccessControl;
using RunFence.Account.Lifecycle;
using RunFence.Acl.Permissions;
using RunFence.Acl.Traverse;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch.Container;

namespace RunFence.Startup;

/// <summary>
/// Runs startup enforcement tasks: applies ACLs/shortcuts/icons to all apps,
/// cleans up expired ephemeral accounts and containers, and grants the interactive
/// desktop user access to the unlock directory.
/// </summary>
public class StartupEnforcementRunner(
    IStartupEnforcementService startupEnforcementService,
    ISessionProvider sessionProvider,
    ISessionSaver sessionSaver,
    IPermissionGrantService permissionGrantService,
    EphemeralAccountService ephemeralAccountService,
    EphemeralContainerService ephemeralContainerService,
    GrantReconciliationService reconciliationService,
    IAppContainerService appContainerService)
{
    /// <summary>
    /// Fixes AppEntry property inconsistencies on the live database. Must run on the UI thread
    /// before snapshotting, so that the fixes are visible in both the snapshot and the live database.
    /// </summary>
    public void FixAppEntryDefaults()
    {
        var database = sessionProvider.GetSession().Database;
        foreach (var app in database.Apps)
        {
            if (app is { IsFolder: true, AclTarget: AclTarget.File })
                app.AclTarget = AclTarget.Folder;
            if (app.AclMode == AclMode.Deny)
                app.AllowedAclEntries = null;
        }
    }

    /// <summary>
    /// Runs enforcement on a snapshot of the database. Safe to call on a background thread.
    /// </summary>
    public EnforcementResult EnforceOnSnapshot(AppDatabase snapshot)
        => startupEnforcementService.Enforce(snapshot);

    /// <summary>
    /// Applies enforcement results to the live database. Must run on the UI thread.
    /// Re-tracks traverse grants, runs group reconciliation, and saves config if needed.
    /// </summary>
    public async Task ApplyEnforcementResult(EnforcementResult result)
    {
        var database = sessionProvider.GetSession().Database;

        foreach (var (appId, timestamp) in result.TimestampUpdates)
        {
            var app = database.Apps.FirstOrDefault(a => a.Id == appId);
            if (app != null)
                app.LastKnownExeTimestamp = timestamp;
        }

        // Re-track traverse grants on the live database. NTFS ACLs were already applied by
        // Enforce() on the snapshot. We write DB entries directly because calling
        // EnsureTraverseAccess again would skip DB tracking (anyAceAdded = false when ACEs exist).
        // AppliedPaths from Enforce() are used so AllAppliedPaths is set correctly for precise reverts.
        bool traverseRetracked = false;
        foreach (var (container, traverseDir, appliedPaths) in result.TraverseGrants)
        {
            var containerSid = appContainerService.GetSid(container.Name);
            var traversePaths = TraversePathsHelper.GetOrCreateTraversePaths(database, containerSid);
            traverseRetracked |= TraversePathsHelper.TrackPath(traversePaths, traverseDir, appliedPaths);
        }

        // Reconcile group membership changes on the live database. Auto-saves if changed.
        bool reconciled = await reconciliationService.ReconcileIfGroupsChanged();

        // Save config if timestamps changed or traverse was re-tracked, but only if reconciliation
        // didn't already save (ReconcileIfGroupsChanged auto-saves when it returns true).
        if ((result.TimestampUpdates.Count > 0 || traverseRetracked) && !reconciled)
            sessionSaver.SaveConfig();
    }

    public void ProcessExpiredContainersAtStartup()
        => ephemeralContainerService.ProcessExpiredContainersAtStartup();

    public void GrantUnlockDirAccess()
    {
        var interactiveSid = SidResolutionHelper.GetInteractiveUserSid();
        if (string.IsNullOrEmpty(interactiveSid))
            return;

        var unlockDir = Path.GetDirectoryName(Constants.UnlockCmdPath);
        if (string.IsNullOrEmpty(unlockDir))
            return;

        permissionGrantService.EnsureAccess(unlockDir, interactiveSid,
            FileSystemRights.ReadAndExecute, confirm: null);
        // Save to persist the grant and traverse tracking. This is idempotent —
        // on subsequent starts AddGrant and EnsureTraverseAccess are no-ops so
        // the save writes the same data as the last startup (no net change).
        sessionSaver.SaveConfig();
    }

    /// <summary>
    /// Triggers ephemeral account expiry processing. Must be called after background services Start().
    /// </summary>
    public void ProcessExpiredAccounts()
        => ephemeralAccountService.ProcessExpiredAccounts();
}