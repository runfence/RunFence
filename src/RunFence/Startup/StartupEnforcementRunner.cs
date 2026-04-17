using System.Collections.Concurrent;
using System.Security.AccessControl;
using RunFence.Account.Lifecycle;
using RunFence.Acl;
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
/// <remarks>Deps above threshold: 6 sequential startup steps with <c>_sessionProvider</c>+<c>_sessionSaver</c>+<c>_log</c> shared across all. Extracting individual steps into classes would create 6 classes each needing the 3 shared deps plus their own 1-2, increasing total wiring and file count with no decoupling (steps must still run in sequence from one orchestrator). Reviewed 2026-04-08.</remarks>
public class StartupEnforcementRunner(
    IStartupEnforcementService startupEnforcementService,
    ISessionProvider sessionProvider,
    ISessionSaver sessionSaver,
    IPathGrantService pathGrantService,
    EphemeralAccountService ephemeralAccountService,
    EphemeralContainerService ephemeralContainerService,
    GrantReconciliationService reconciliationService,
    IAppContainerService appContainerService,
    IInteractiveUserResolver interactiveUserResolver,
    ILoggingService log,
    EnforcementResultApplier enforcementResultApplier)
{
    /// <summary>
    /// Re-derives container SIDs when the interactive user has changed since last startup.
    /// Container SIDs vary per interactive user; cached SIDs from JSON remain valid when the user
    /// is the same. Must run on the UI thread before snapshotting.
    /// </summary>
    public void RefreshContainerSidsIfUserChanged()
    {
        var currentIuSid = interactiveUserResolver.GetInteractiveUserSid();
        if (string.IsNullOrEmpty(currentIuSid))
            return;

        var db = sessionProvider.GetSession().Database;
        if (string.Equals(currentIuSid, db.Settings.LastInteractiveUserSid, StringComparison.OrdinalIgnoreCase))
            return;

        // Derive SIDs in parallel (P/Invoke calls are the slow part),
        // then write to DB entries sequentially (DB is not thread-safe).
        var results = new ConcurrentBag<(AppContainerEntry Container, string Sid)>();
        Parallel.ForEach(db.AppContainers, container =>
        {
            try
            {
                var sid = appContainerService.GetSid(container.Name);
                results.Add((container, sid));
            }
            catch (Exception ex) { log.Warn($"Container SID resolution failed for '{container.Name}': {ex.Message}"); }
        });
        foreach (var (container, sid) in results)
            container.Sid = sid;

        db.Settings.LastInteractiveUserSid = currentIuSid;
        sessionSaver.SaveConfig();
    }

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
    /// Updates timestamps, re-tracks traverse grants, runs group reconciliation, and saves config if needed.
    /// </summary>
    public async Task ApplyEnforcementResult(EnforcementResult result)
    {
        var database = sessionProvider.GetSession().Database;

        // Apply timestamp updates and re-track traverse grants on the live database.
        // NTFS ACLs were already applied by Enforce() on the snapshot; DB entries are written
        // directly because EnsureTraverseAccess would skip tracking when ACEs already exist.
        var (timestampsChanged, traverseRetracked) = enforcementResultApplier.ApplyToDatabase(result, database);

        // Reconcile group membership changes on the live database. Auto-saves if changed.
        bool reconciled = await reconciliationService.ReconcileIfGroupsChanged();

        // Save config if timestamps changed or traverse was re-tracked, but only if reconciliation
        // didn't already save (ReconcileIfGroupsChanged auto-saves when it returns true).
        if ((timestampsChanged || traverseRetracked) && !reconciled)
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

        var result = pathGrantService.EnsureAccess(interactiveSid, unlockDir,
            FileSystemRights.ReadAndExecute, confirm: null);
        if (result.DatabaseModified)
            sessionSaver.SaveConfig();
    }

    /// <summary>
    /// Triggers ephemeral account expiry processing. Must be called after background services Start().
    /// </summary>
    public void ProcessExpiredAccounts()
        => ephemeralAccountService.ProcessExpiredAccounts();
}