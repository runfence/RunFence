using RunFence.Account;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Infrastructure;
using RunFence.Persistence;

namespace RunFence.SidMigration;

public class SidMigrationApplicationService(
    IAppConfigService appConfigService,
    ILoggingService log,
    IFirewallEnforcementOrchestrator firewallEnforcementOrchestrator,
    SidMigrationMutationApplier mutationApplier,
    UiThreadDatabaseAccessor dbAccessor)
{
    public async Task<SidMigrationApplicationApplyResult> ApplyAsync(
        IReadOnlyList<SidMigrationMapping> mappings,
        IReadOnlyList<string> sidsToDelete,
        SessionContext session)
    {
        var retryIntentWritten = mappings.Count > 0 || sidsToDelete.Count > 0;
        var applyState = new SidMigrationApplyState();
        var runtimeSnapshot = new SidMigrationRuntimeSnapshot(
            dbAccessor.CreateSnapshot(),
            CredentialStoreCloneHelper.CloneStore(session.CredentialStore),
            appConfigService.CaptureRuntimeStateSnapshot());

        try
        {
            await Task.Run(() => mutationApplier.Apply(mappings, sidsToDelete, session, applyState));
        }
        catch (OperationCanceledException)
        {
            RestoreState(session, runtimeSnapshot);
            return new SidMigrationApplicationApplyResult(
                new SidMigrationWorkflowResult(
                    SidMigrationWorkflowStatus.Canceled,
                    AppliedFilesystemChanges: applyState.FilesystemChangesApplied,
                    AppliedAppEnforcementChanges: applyState.AppEnforcementApplied,
                    SavedDatabase: false,
                    RetryStateWritten: retryIntentWritten,
                    Errors: []),
                applyState.Messages,
                null);
        }
        catch (Exception ex)
        {
            applyState.Errors.Add(ex.Message);
            if (!applyState.ExternalMutationStarted)
            {
                string? rollbackError = null;
                try
                {
                    RestoreState(session, runtimeSnapshot);
                    appConfigService.ReencryptAndSaveAll(session.CredentialStore, session.Database, session.PinDerivedKey);
                }
                catch (Exception rollbackException)
                {
                    rollbackError = rollbackException.Message;
                    log.Error("SID migration rollback save failed.", rollbackException);
                }

                if (rollbackError != null)
                {
                    applyState.Errors.Add($"Rollback save failed: {rollbackError}");
                    return new SidMigrationApplicationApplyResult(
                        new SidMigrationWorkflowResult(
                            SidMigrationWorkflowStatus.RollbackFailed,
                            AppliedFilesystemChanges: applyState.FilesystemChangesApplied,
                            AppliedAppEnforcementChanges: applyState.AppEnforcementApplied,
                            SavedDatabase: false,
                            RetryStateWritten: retryIntentWritten,
                            Errors: applyState.Errors),
                        applyState.Messages,
                        rollbackError);
                }

                return new SidMigrationApplicationApplyResult(
                    new SidMigrationWorkflowResult(
                        SidMigrationWorkflowStatus.Failed,
                        AppliedFilesystemChanges: applyState.FilesystemChangesApplied,
                        AppliedAppEnforcementChanges: applyState.AppEnforcementApplied,
                        SavedDatabase: false,
                        RetryStateWritten: retryIntentWritten,
                        Errors: applyState.Errors),
                    applyState.Messages,
                    null);
            }

            applyState.PostMutationFailure = true;
            applyState.Messages.Add($"SID migration warning: {ex.Message}");
            log.Error("SID migration failed after external mutation started; preserving migrated state for recovery.", ex);
        }

        try
        {
            appConfigService.ReencryptAndSaveAll(session.CredentialStore, session.Database, session.PinDerivedKey);
        }
        catch (Exception ex)
        {
            applyState.Errors.Add(ex.Message);
            return new SidMigrationApplicationApplyResult(
                new SidMigrationWorkflowResult(
                    retryIntentWritten
                        ? SidMigrationWorkflowStatus.AppliedButSaveFailed
                        : SidMigrationWorkflowStatus.Failed,
                    AppliedFilesystemChanges: applyState.FilesystemChangesApplied,
                    AppliedAppEnforcementChanges: applyState.AppEnforcementApplied,
                    SavedDatabase: false,
                    RetryStateWritten: retryIntentWritten,
                    Errors: applyState.Errors),
                applyState.Messages,
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
                applyState.Messages.Add($"Firewall enforcement warning after SID migration: {scope}: {failure.Message}");
            }
        }
        catch (Exception ex)
        {
            // Firewall enforcement failures are warning/retry behavior and must not block saved migration state.
            log.Warn($"Firewall enforcement after SID migration failed: {ex.Message}");
            applyState.Messages.Add($"Firewall enforcement warning after SID migration: {ex.Message}");
        }

        if (applyState.PostMutationFailure)
        {
            return new SidMigrationApplicationApplyResult(
                new SidMigrationWorkflowResult(
                    SidMigrationWorkflowStatus.Failed,
                    AppliedFilesystemChanges: applyState.FilesystemChangesApplied,
                    AppliedAppEnforcementChanges: applyState.AppEnforcementApplied,
                    SavedDatabase: true,
                    RetryStateWritten: retryIntentWritten,
                    Errors: applyState.Errors),
                applyState.Messages,
                null);
        }

        return new SidMigrationApplicationApplyResult(
            new SidMigrationWorkflowResult(
                SidMigrationWorkflowStatus.Succeeded,
                AppliedFilesystemChanges: applyState.FilesystemChangesApplied,
                AppliedAppEnforcementChanges: applyState.AppEnforcementApplied,
                SavedDatabase: true,
                RetryStateWritten: retryIntentWritten,
                Errors: []),
            applyState.Messages,
            null);
    }

    private void RestoreState(
        SessionContext session,
        SidMigrationRuntimeSnapshot runtimeSnapshot)
    {
        session.Database.ReplaceWithSnapshot(runtimeSnapshot.DatabaseBefore);
        session.CredentialStore.ReplaceWithSnapshot(runtimeSnapshot.CredentialsBefore);
        appConfigService.RestoreRuntimeStateSnapshot(runtimeSnapshot.AppConfigStateBefore);
    }
}
