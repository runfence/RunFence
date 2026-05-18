using RunFence.Acl;
using RunFence.Apps;
using RunFence.Apps.Shortcuts;
using RunFence.Account;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Infrastructure;
using RunFence.Persistence;

namespace RunFence.SidMigration;

public class SidMigrationApplicationService(
    ISidMigrationService sidMigrationService,
    IAppConfigService appConfigService,
    ILoggingService log,
    IAclService aclService,
    IShortcutDiscoveryService shortcutDiscovery,
    AppEntryEnforcementHelper enforcementHelper,
    IFirewallCleanupService firewallCleanupService,
    IFirewallEnforcementOrchestrator firewallEnforcementOrchestrator,
    SidDeletionHandler sidDeletionHandler,
    UiThreadDatabaseAccessor dbAccessor)
{
    public async Task<(SidMigrationWorkflowResult workflow, List<string> messages, string? saveError)> ApplyAsync(
        IReadOnlyList<SidMigrationMapping> mappings,
        IReadOnlyList<string> sidsToDelete,
        SessionContext session)
    {
        var messages = new List<string>();
        var errors = new List<string>();
        var retryIntentWritten = mappings.Count > 0 || sidsToDelete.Count > 0;
        var appEnforcementApplied = false;
        var filesystemChangesApplied = false;
        var externalMutationStarted = false;
        var postMutationFailure = false;
        var databaseBefore = dbAccessor.CreateSnapshot();
        var credentialsBefore = CredentialStoreCloneHelper.CloneStore(session.CredentialStore);
        var appConfigStateBefore = appConfigService.CaptureRuntimeStateSnapshot();

        try
        {
            await Task.Run(() =>
            {
                if (mappings.Count > 0)
                {
                    MigrationCounts counts = default;
                    dbAccessor.Write(_ => { counts = sidMigrationService.MigrateAppData(mappings, session.CredentialStore); });
                    messages.Add($"Migrated {counts.Credentials} credential(s), {counts.Apps} app(s), {counts.IpcCallers} IPC caller(s), {counts.AllowEntries} allow entry/entries.");

                    foreach (var mapping in mappings)
                    {
                        try
                        {
                            externalMutationStarted = true;
                            firewallCleanupService.RemoveAllRules(mapping.OldSid);
                        }
                        catch (Exception ex)
                        {
                            log.Warn($"Failed to remove firewall rules for old SID '{mapping.OldSid}': {ex.Message}");
                        }
                    }

                    var migratedSids = mappings.Select(m => m.NewSid).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var snapshot = dbAccessor.CreateSnapshot();
                    var migratedApps = snapshot.Apps.Where(app => migratedSids.Contains(app.AccountSid)).ToList();
                    var shortcutCache = shortcutDiscovery.CreateTraversalCacheIfNeeded(migratedApps);

                    foreach (var app in migratedApps)
                    {
                        try
                        {
                            externalMutationStarted = true;
                            enforcementHelper.RevertChanges(app, snapshot.Apps, shortcutCache);
                            enforcementHelper.ApplyChanges(app, snapshot.Apps, shortcutCache);
                            dbAccessor.Write(db =>
                            {
                                var liveApp = db.Apps.FirstOrDefault(a => string.Equals(a.Id, app.Id, StringComparison.OrdinalIgnoreCase));
                                if (liveApp != null)
                                    liveApp.EnforcementRetryStatus = null;
                            });
                        }
                        catch (Exception ex)
                        {
                            var retryStatus = new AppEnforcementRetryStatus(ex.Message, DateTime.UtcNow);
                            dbAccessor.Write(db =>
                            {
                                var liveApp = db.Apps.FirstOrDefault(a => string.Equals(a.Id, app.Id, StringComparison.OrdinalIgnoreCase));
                                if (liveApp != null)
                                    liveApp.EnforcementRetryStatus = retryStatus;
                            });
                            messages.Add($"App enforcement retry scheduled for '{app.Name}' ({app.Id}): {ex.Message}");
                            log.Warn($"SID migration app enforcement failed for '{app.Name}' ({app.Id}): {ex.Message}");
                        }
                    }

                    try
                    {
                        externalMutationStarted = true;
                        aclService.RecomputeAllAncestorAcls(snapshot.Apps);
                    }
                    catch (Exception ex)
                    {
                        messages.Add($"Ancestor ACL recompute retry scheduled after SID migration: {ex.Message}");
                        log.Warn($"SID migration ancestor ACL recompute failed: {ex.Message}");
                    }

                    appEnforcementApplied = migratedApps.Count > 0;
                }

                if (sidsToDelete.Count > 0)
                {
                    var snapshot = dbAccessor.CreateSnapshot();
                    var affectedApps = sidsToDelete.SelectMany(sid => snapshot.Apps.Where(a => string.Equals(a.AccountSid, sid, StringComparison.OrdinalIgnoreCase)));
                    var shortcutCache = shortcutDiscovery.CreateTraversalCacheIfNeeded(affectedApps);
                    externalMutationStarted = true;
                    sidDeletionHandler.Apply(sidsToDelete, snapshot, session.CredentialStore, shortcutCache, messages);
                    filesystemChangesApplied = true;
                }
            });
        }
        catch (OperationCanceledException)
        {
            RestoreState(session, databaseBefore, credentialsBefore, appConfigStateBefore);
            return (new SidMigrationWorkflowResult(
                    SidMigrationWorkflowStatus.Canceled,
                    AppliedFilesystemChanges: filesystemChangesApplied,
                    AppliedAppEnforcementChanges: appEnforcementApplied,
                    SavedDatabase: false,
                    RetryStateWritten: retryIntentWritten,
                    Errors: []),
                messages,
                null);
        }
        catch (Exception ex)
        {
            if (!externalMutationStarted)
            {
                errors.Add(ex.Message);
                if (!TryRollbackAndSave(session, databaseBefore, credentialsBefore, appConfigStateBefore, out var rollbackError))
                {
                    if (rollbackError != null)
                        errors.Add($"Rollback save failed: {rollbackError}");
                    return (new SidMigrationWorkflowResult(
                            SidMigrationWorkflowStatus.RollbackFailed,
                            AppliedFilesystemChanges: filesystemChangesApplied,
                            AppliedAppEnforcementChanges: appEnforcementApplied,
                            SavedDatabase: false,
                            RetryStateWritten: retryIntentWritten,
                            Errors: errors),
                        messages,
                        rollbackError);
                }

                return (new SidMigrationWorkflowResult(
                        SidMigrationWorkflowStatus.Failed,
                        AppliedFilesystemChanges: filesystemChangesApplied,
                        AppliedAppEnforcementChanges: appEnforcementApplied,
                        SavedDatabase: false,
                        RetryStateWritten: retryIntentWritten,
                        Errors: errors),
                    messages,
                    null);
            }

            errors.Add(ex.Message);
            postMutationFailure = true;
            messages.Add($"SID migration warning: {ex.Message}");
            log.Error("SID migration failed after external mutation started; preserving migrated state for recovery.", ex);
        }

        try
        {
            appConfigService.ReencryptAndSaveAll(session.CredentialStore, session.Database, session.PinDerivedKey);
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
            return (new SidMigrationWorkflowResult(
                    retryIntentWritten
                        ? SidMigrationWorkflowStatus.AppliedButSaveFailed
                        : SidMigrationWorkflowStatus.Failed,
                    AppliedFilesystemChanges: filesystemChangesApplied,
                    AppliedAppEnforcementChanges: appEnforcementApplied,
                    SavedDatabase: false,
                    RetryStateWritten: retryIntentWritten,
                    Errors: errors),
                messages,
                ex.Message);
        }

        try
        {
            var snapshot = dbAccessor.CreateSnapshot();
            var enforcementResult = firewallEnforcementOrchestrator.EnforceAll(snapshot);
            foreach (var failure in enforcementResult.Failures)
            {
                var scope = string.IsNullOrWhiteSpace(failure.AccountSid)
                    ? failure.Layer.ToString()
                    : $"{failure.Layer} ({failure.AccountSid})";
                messages.Add($"Firewall enforcement warning after SID migration: {scope}: {failure.Message}");
            }
        }
        catch (Exception ex)
        {
            // Firewall enforcement failures are warning/retry behavior and must not block saved migration state.
            log.Warn($"Firewall enforcement after SID migration failed: {ex.Message}");
            messages.Add($"Firewall enforcement warning after SID migration: {ex.Message}");
        }

        if (postMutationFailure)
        {
            return (new SidMigrationWorkflowResult(
                    SidMigrationWorkflowStatus.Failed,
                    AppliedFilesystemChanges: filesystemChangesApplied,
                    AppliedAppEnforcementChanges: appEnforcementApplied,
                    SavedDatabase: true,
                    RetryStateWritten: retryIntentWritten,
                    Errors: errors),
                messages,
                null);
        }

        return (new SidMigrationWorkflowResult(
                SidMigrationWorkflowStatus.Succeeded,
                AppliedFilesystemChanges: filesystemChangesApplied,
                AppliedAppEnforcementChanges: appEnforcementApplied,
                SavedDatabase: true,
                RetryStateWritten: retryIntentWritten,
                Errors: []),
            messages,
            null);
    }

    private bool TryRollbackAndSave(
        SessionContext session,
        AppDatabase databaseBefore,
        CredentialStore credentialsBefore,
        AppConfigRuntimeStateSnapshot appConfigStateBefore,
        out string? rollbackError)
    {
        rollbackError = null;
        try
        {
            RestoreState(session, databaseBefore, credentialsBefore, appConfigStateBefore);
            appConfigService.ReencryptAndSaveAll(session.CredentialStore, session.Database, session.PinDerivedKey);
            return true;
        }
        catch (Exception ex)
        {
            rollbackError = ex.Message;
            log.Error("SID migration rollback save failed.", ex);
            return false;
        }
    }

    private void RestoreState(
        SessionContext session,
        AppDatabase databaseBefore,
        CredentialStore credentialsBefore,
        AppConfigRuntimeStateSnapshot appConfigStateBefore)
    {
        session.Database.ReplaceWithSnapshot(databaseBefore);
        session.CredentialStore.ReplaceWithSnapshot(credentialsBefore);
        appConfigService.RestoreRuntimeStateSnapshot(appConfigStateBefore);
    }
}
