using RunFence.Acl;
using RunFence.Account.OrphanedProfiles;
using RunFence.Apps;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Infrastructure;

namespace RunFence.SidMigration;

public class SidMigrationMutationApplier(
    ISidMigrationService sidMigrationService,
    SidMigrationDataMappingPlanner dataMappingPlanner,
    SidMigrationDeletionPlanner deletionPlanner,
    ILoggingService log,
    IAclService aclService,
    IShortcutDiscoveryService shortcutDiscovery,
    AppEntryEnforcementCoordinator enforcementCoordinator,
    IFirewallCleanupService firewallCleanupService,
    IShortcutService shortcutService,
    IBesideTargetShortcutService besideTargetShortcutService,
    IOrphanedProfileService orphanedProfileService,
    IGrantAccountCleanupService grantAccountCleanupService,
    UiThreadDatabaseAccessor dbAccessor)
{
    public void Apply(
        IReadOnlyList<SidMigrationMapping> mappings,
        IReadOnlyList<string> sidsToDelete,
        SessionContext session,
        SidMigrationApplyState state)
    {
        if (mappings.Count > 0)
            state.AppEnforcementApplied = ApplyDataMappings(mappings, session, state);

        if (sidsToDelete.Count > 0)
        {
            ApplySidDeletions(sidsToDelete, session, state);
            state.FilesystemChangesApplied = true;
        }
    }

    private bool ApplyDataMappings(
        IReadOnlyList<SidMigrationMapping> mappings,
        SessionContext session,
        SidMigrationApplyState state)
    {
        var mappingPlan = dataMappingPlanner.BuildPlan(mappings, session.CredentialStore, dbAccessor.CreateSnapshot());
        ApplyDataStoreMutation(mappingPlan, session, state);
        ApplyOldFirewallRuleCleanup(mappingPlan, state);

        var snapshot = dbAccessor.CreateSnapshot();
        var migratedApps = GetMigratedApps(mappingPlan, snapshot);
        ReapplyMigratedAppEnforcement(snapshot, migratedApps, state);
        RecomputeAncestorAcls(snapshot, state);
        return migratedApps.Count > 0;
    }

    private void ApplyDataStoreMutation(
        SidMigrationDataMappingPlan mappingPlan,
        SessionContext session,
        SidMigrationApplyState state)
    {
        dbAccessor.Write(_ => sidMigrationService.MigrateAppData(mappingPlan.Mappings, session.CredentialStore));
        state.Messages.Add(dataMappingPlanner.FormatMigrationMessage(mappingPlan.Counts));
    }

    private void ApplyOldFirewallRuleCleanup(
        SidMigrationDataMappingPlan mappingPlan,
        SidMigrationApplyState state)
    {
        foreach (var mapping in mappingPlan.Mappings)
        {
            try
            {
                state.ExternalMutationStarted = true;
                firewallCleanupService.RemoveAllRules(mapping.OldSid);
            }
            catch (Exception ex)
            {
                log.Warn($"Failed to remove firewall rules for old SID '{mapping.OldSid}': {ex.Message}");
            }
        }
    }

    private List<AppEntry> GetMigratedApps(
        SidMigrationDataMappingPlan mappingPlan,
        AppDatabase snapshot)
    {
        var migratedSids = mappingPlan.Mappings
            .Select(mapping => mapping.NewSid)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return snapshot.Apps
            .Where(app => migratedSids.Contains(app.AccountSid))
            .ToList();
    }

    private void ReapplyMigratedAppEnforcement(
        AppDatabase snapshot,
        IReadOnlyList<AppEntry> migratedApps,
        SidMigrationApplyState state)
    {
        var shortcutCache = shortcutDiscovery.CreateTraversalCacheIfNeeded(migratedApps);
        foreach (var app in migratedApps)
            ReapplyAppEnforcement(snapshot, shortcutCache, app, state);
    }

    private void ReapplyAppEnforcement(
        AppDatabase snapshot,
        ShortcutTraversalCache shortcutCache,
        AppEntry app,
        SidMigrationApplyState state)
    {
        try
        {
            state.ExternalMutationStarted = true;
            enforcementCoordinator.RevertChanges(app, snapshot.Apps, shortcutCache);
            enforcementCoordinator.ApplyChanges(app, snapshot.Apps, shortcutCache);
            UpdateAppEnforcementRetryStatus(app.Id, null);
        }
        catch (Exception ex)
        {
            var retryStatus = new AppEnforcementRetryStatus(ex.Message, DateTime.UtcNow);
            UpdateAppEnforcementRetryStatus(app.Id, retryStatus);
            state.Messages.Add($"App enforcement retry scheduled for '{app.Name}' ({app.Id}): {ex.Message}");
            log.Warn($"SID migration app enforcement failed for '{app.Name}' ({app.Id}): {ex.Message}");
        }
    }

    private void UpdateAppEnforcementRetryStatus(
        string appId,
        AppEnforcementRetryStatus? retryStatus)
    {
        dbAccessor.Write(db =>
        {
            var liveApp = db.Apps.FirstOrDefault(candidate => string.Equals(candidate.Id, appId, StringComparison.OrdinalIgnoreCase));
            if (liveApp != null)
                liveApp.EnforcementRetryStatus = retryStatus;
        });
    }

    private void RecomputeAncestorAcls(
        AppDatabase snapshot,
        SidMigrationApplyState state)
    {
        try
        {
            state.ExternalMutationStarted = true;
            aclService.RecomputeAllAncestorAcls(snapshot.Apps);
        }
        catch (Exception ex)
        {
            state.Messages.Add($"Ancestor ACL recompute retry scheduled after SID migration: {ex.Message}");
            log.Warn($"SID migration ancestor ACL recompute failed: {ex.Message}");
        }
    }

    private void ApplySidDeletions(
        IReadOnlyList<string> sidsToDelete,
        SessionContext session,
        SidMigrationApplyState state)
    {
        var snapshot = dbAccessor.CreateSnapshot();
        var deletionPlan = deletionPlanner.BuildPlan(sidsToDelete, snapshot);
        var shortcutCache = shortcutDiscovery.CreateTraversalCacheIfNeeded(deletionPlan.AppsNeedingShortcutCleanup);

        state.ExternalMutationStarted = true;
        RevertDeletedSidAppChanges(deletionPlan, snapshot, shortcutCache);
        CleanupDeletedSidProfiles(deletionPlan);
        CleanupDeletedSidFirewallRules(deletionPlan);
        CleanupDeletedSidGrants(deletionPlan, snapshot);
        DeleteSidData(deletionPlan, session.CredentialStore, state.Messages);
        RecomputeDeletedSidAncestorAcls();
    }

    private void RevertDeletedSidAppChanges(
        SidMigrationDeletionPlan deletionPlan,
        AppDatabase snapshot,
        ShortcutTraversalCache shortcutCache)
    {
        foreach (var sid in deletionPlan.SidsToDelete)
        {
            var affectedApps = snapshot.Apps
                .Where(app => string.Equals(app.AccountSid, sid, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var app in affectedApps)
            {
                TryRevertDeletedSidAcl(snapshot, app);
                TryRevertDeletedSidShortcuts(shortcutCache, app);
                TryRemoveDeletedSidBesideTargetShortcut(app);
            }
        }
    }

    private void TryRevertDeletedSidAcl(
        AppDatabase snapshot,
        AppEntry app)
    {
        try
        {
            if (app.RestrictAcl)
                aclService.RevertAcl(app, snapshot.Apps);
        }
        catch (Exception ex)
        {
            log.Warn($"Failed to revert ACL for {app.Name}: {ex.Message}");
        }
    }

    private void TryRevertDeletedSidShortcuts(
        ShortcutTraversalCache shortcutCache,
        AppEntry app)
    {
        try
        {
            if (app.ManageShortcuts)
                shortcutService.RevertShortcuts(app, shortcutCache);
        }
        catch (Exception ex)
        {
            log.Warn($"Failed to revert shortcuts for {app.Name}: {ex.Message}");
        }
    }

    private void TryRemoveDeletedSidBesideTargetShortcut(AppEntry app)
    {
        try
        {
            besideTargetShortcutService.RemoveBesideTargetShortcut(app);
        }
        catch (Exception ex)
        {
            log.Warn($"Failed to remove shortcut for {app.Name}: {ex.Message}");
        }
    }

    private void CleanupDeletedSidProfiles(SidMigrationDeletionPlan deletionPlan)
    {
        foreach (var sid in deletionPlan.SidsToDelete)
        {
            try
            {
                orphanedProfileService.CleanupLogonScripts(sid);
            }
            catch (Exception ex)
            {
                log.Warn($"Failed to cleanup logon scripts for {sid}: {ex.Message}");
            }
        }
    }

    private void CleanupDeletedSidFirewallRules(SidMigrationDeletionPlan deletionPlan)
    {
        foreach (var sid in deletionPlan.SidsToDelete)
        {
            try
            {
                firewallCleanupService.RemoveAllRules(sid);
            }
            catch (Exception ex)
            {
                log.Warn($"Failed to remove firewall rules for SID '{sid}': {ex.Message}");
            }
        }
    }

    private void CleanupDeletedSidGrants(
        SidMigrationDeletionPlan deletionPlan,
        AppDatabase snapshot)
    {
        foreach (var sid in deletionPlan.SidsToDelete)
        {
            if (snapshot.GetAccount(sid)?.Grants is not { Count: > 0 })
                continue;

            try
            {
                var result = grantAccountCleanupService.RemoveAll(sid);
                foreach (var warning in result.Warnings)
                    log.Warn($"Grant cleanup warning for SID '{sid}': {GrantApplyFailureFormatter.Format(warning)}");
            }
            catch (Exception ex)
            {
                log.Warn($"Failed to revert grants for SID '{sid}': {ex.Message}");
            }
        }
    }

    private void DeleteSidData(
        SidMigrationDeletionPlan deletionPlan,
        CredentialStore credentialStore,
        List<string> messages)
    {
        var deletedCredentials = 0;
        var deletedApps = 0;
        var deletedCallers = 0;

        dbAccessor.Write(_ =>
        {
            var result = sidMigrationService.DeleteSidsFromAppData(deletionPlan.SidsToDelete, credentialStore);
            deletedCredentials = result.credentials;
            deletedApps = result.apps;
            deletedCallers = result.ipcCallers;
        });

        messages.Add($"Deleted {deletedCredentials} credential(s), {deletedApps} app(s), {deletedCallers} IPC caller(s).");
    }

    private void RecomputeDeletedSidAncestorAcls()
    {
        var postDeleteSnapshot = dbAccessor.CreateSnapshot();
        aclService.RecomputeAllAncestorAcls(postDeleteSnapshot.Apps);
    }
}
