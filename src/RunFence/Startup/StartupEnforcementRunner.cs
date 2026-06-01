using System.Collections.Concurrent;
using System.Security.AccessControl;
using RunFence.Account.Lifecycle;
using RunFence.Acl;
using RunFence.Acl.Permissions;
using RunFence.Acl.Traverse;
using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Helpers;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch.Container;

namespace RunFence.Startup;

/// <summary>
/// Runs startup enforcement tasks: applies ACLs/shortcuts/icons to all apps,
/// cleans up expired ephemeral accounts and containers, and grants the interactive
/// desktop user access to the unlock directory.
/// </summary>
/// <remarks>Deps above threshold: 11 deps, 135 lines: extracting cleanup methods (3 methods, 4 deps) creates a 60-line class that still needs session/save/log (3 deps shared with remaining class). Net: 2 classes × 8 deps each vs 1 × 11 — more total wiring for 135 lines of sequential logic. Reviewed 2026-04-08.</remarks>
public class StartupEnforcementRunner(
    IStartupEnforcementService startupEnforcementService,
    ISessionProvider sessionProvider,
    ISessionSaver sessionSaver,
    IGrantMutatorService grantMutatorService,
    EphemeralAccountService ephemeralAccountService,
    EphemeralContainerService ephemeralContainerService,
    GrantReconciliationService reconciliationService,
    IAppContainerService appContainerService,
    IInteractiveUserResolver interactiveUserResolver,
    ILoggingService log,
    EnforcementResultApplier enforcementResultApplier,
    AppEntryPathRepairCoordinator appEntryPathRepairCoordinator)
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
        if (SidComparer.SidEquals(currentIuSid, db.Settings.LastInteractiveUserSid))
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
    public StartupAppEntryDefaultRepairResult FixAppEntryDefaults()
    {
        var database = sessionProvider.GetSession().Database;
        var changedAppIds = new List<string>();
        foreach (var app in database.Apps)
        {
            bool changed = false;
            if (app is { IsFolder: true, AclTarget: AclTarget.File })
            {
                app.AclTarget = AclTarget.Folder;
                changed = true;
            }
            if (app.AclMode == AclMode.Deny)
            {
                if (app.AllowedAclEntries != null)
                    changed = true;
                app.AllowedAclEntries = null;
            }

            if (changed && !string.IsNullOrEmpty(app.Id))
                changedAppIds.Add(app.Id);
        }

        return new StartupAppEntryDefaultRepairResult(
            Changed: changedAppIds.Count > 0,
            ChangedAppIds: changedAppIds,
            Warnings: []);
    }

    /// <summary>
    /// Repairs missing trusted app entry paths on the live database before snapshotting or manual reapply.
    /// Must run on the UI thread because repairs can mutate and persist the live session database.
    /// </summary>
    public StartupAppEntryPathRepairResult RepairMissingAppEntryPaths()
    {
        var database = sessionProvider.GetSession().Database;
        var changedAppIds = new List<string>();
        var warnings = new List<string>();

        foreach (var app in database.Apps)
        {
            if (app.IsUrlScheme)
                continue;

            var repairResult = appEntryPathRepairCoordinator.RepairIfNeeded(app);
            if (repairResult.SaveFailed)
            {
                return new StartupAppEntryPathRepairResult(
                    Changed: changedAppIds.Count > 0,
                    ChangedAppIds: changedAppIds,
                    Warnings: warnings,
                    SaveFailureMessage: BuildSaveFailureMessage(app, repairResult.WarningMessage));
            }

            if (!repairResult.Repaired)
                continue;

            if (!string.IsNullOrEmpty(repairResult.App.Id))
                changedAppIds.Add(repairResult.App.Id);

            if (!string.IsNullOrWhiteSpace(repairResult.WarningMessage))
                warnings.Add($"{GetAppLabel(repairResult.App)}: {repairResult.WarningMessage}");
        }

        return new StartupAppEntryPathRepairResult(
            Changed: changedAppIds.Count > 0,
            ChangedAppIds: changedAppIds,
            Warnings: warnings,
            SaveFailureMessage: null);
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

    public Task ProcessExpiredContainersAtStartup()
        => ephemeralContainerService.ProcessExpiredContainersAtStartup();

    public void GrantUnlockDirAccess()
    {
        var interactiveSid = interactiveUserResolver.GetInteractiveUserSid();
        if (string.IsNullOrEmpty(interactiveSid))
            return;

        var unlockDir = Path.GetDirectoryName(PathConstants.UnlockCmdPath);
        if (string.IsNullOrEmpty(unlockDir))
            return;

        _ = grantMutatorService.EnsureAccess(interactiveSid, unlockDir,
            FileSystemRights.ReadAndExecute, confirm: null, unelevated: true);
        _ = grantMutatorService.EnsureAccess(AclHelper.LowIntegritySid, unlockDir,
            FileSystemRights.ReadAndExecute, confirm: null, unelevated: true);
        _ = grantMutatorService.EnsureAccess(AclHelper.AllApplicationPackagesSid, unlockDir,
            FileSystemRights.ReadAndExecute, confirm: null, unelevated: true);
    }

    /// <summary>
    /// Triggers ephemeral account expiry processing. Must be called after background services Start().
    /// </summary>
    public Task ProcessExpiredAccountsAsync()
        => ephemeralAccountService.ProcessExpiredAccountsAsync();

    private static string BuildSaveFailureMessage(AppEntry app, string? detail)
        => string.IsNullOrWhiteSpace(detail)
            ? $"Application '{GetAppLabel(app)}' could not persist its repaired path."
            : $"Application '{GetAppLabel(app)}' could not persist its repaired path:{Environment.NewLine}{Environment.NewLine}{detail}";

    private static string GetAppLabel(AppEntry app)
        => string.IsNullOrWhiteSpace(app.Name) ? app.Id : app.Name;
}
