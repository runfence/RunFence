using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.Apps;

namespace RunFence.RunAs;

/// <summary>
/// Handles the commit and rollback of app entry edit operations:
/// reassigns config, saves all configs, and notifies data changed.
/// </summary>
public class AppEditCommitService(
    IAppConfigService appConfigService,
    IDataChangeNotifier dataChangeNotifier,
    ILoggingService log,
    SessionContext session,
    RunAsAppEntryManager appEntryManager,
    AppEntryPersistenceOrchestrator persistenceOrchestrator,
    IAppStateProvider appState) : IAppEditCommitService
{
    public RunAsAppEntryPersistenceResult Commit(RunAsAppEditCommitRequest request)
    {
        try
        {
            if (request.PreviousApp != null)
            {
                return CommitExistingApp(request);
            }

            return persistenceOrchestrator.PersistNewAppEntry(request.NewApp, request.ConfigPath);
        }
        catch (Exception ex)
        {
            log.Error("AppEditCommitService: commit failed", ex);
            return new RunAsAppEntryPersistenceResult(
                RunAsAppEntryPersistenceStatus.SaveFailed,
                request.NewApp,
                ex.Message);
        }
    }

    private RunAsAppEntryPersistenceResult CommitExistingApp(RunAsAppEditCommitRequest request)
    {
        var newApp = request.NewApp;
        var previousApp = request.PreviousApp!;
        if (!string.Equals(newApp.Id, previousApp.Id, StringComparison.Ordinal))
        {
            return new RunAsAppEntryPersistenceResult(
                RunAsAppEntryPersistenceStatus.SaveFailed,
                newApp,
                "Existing application edits must preserve the application ID.");
        }

        var index = appState.Database.Apps.FindIndex(a => a.Id == previousApp.Id);
        if (index < 0)
        {
            return new RunAsAppEntryPersistenceResult(
                RunAsAppEntryPersistenceStatus.SaveFailed,
                newApp,
                "The application no longer exists.");
        }

        var originalConfigPath = request.PreviousConfigPath;
        var changeSet = request.ChangeSet;
        var cleanupResult = appEntryManager.RevertAppChanges(previousApp, changeSet);
        if (cleanupResult.Status != RunAsAppEntryPersistenceStatus.Succeeded)
            return cleanupResult;

        try
        {
            AppEntryShortcutProtectionStateHelper.ApplyExistingEditState(previousApp, newApp, changeSet);
            appState.Database.Apps[index] = newApp;
            appConfigService.AssignApp(newApp.Id, request.ConfigPath);
            SaveForScope(changeSet.ConfigSaveScope, newApp.Id);
        }
        catch (Exception mutationOrSaveEx)
        {
            log.Error("AppEditCommitService: save failed for existing app edit", mutationOrSaveEx);
            var restoreFailures = new List<string>();
            try
            {
                appState.Database.Apps[index] = previousApp;
                appConfigService.AssignApp(previousApp.Id, originalConfigPath);
                try
                {
                    SaveForScope(changeSet.ConfigSaveScope, previousApp.Id);
                }
                catch (Exception restoreSaveException)
                {
                    log.Error("AppEditCommitService: failed to restore previous persisted app state after save failure",
                        restoreSaveException);
                    restoreFailures.Add(restoreSaveException.Message);
                }

                var restoreResult = appEntryManager.ApplyAppChanges(previousApp, changeSet);
                if (restoreResult.Status != RunAsAppEntryPersistenceStatus.Succeeded)
                {
                    log.Error("AppEditCommitService: failed to restore previous app state after save failure",
                        new InvalidOperationException(
                            restoreResult.WarningMessage ??
                            restoreResult.ErrorMessage ??
                            "Unknown restore failure."));
                    restoreFailures.Add(
                        restoreResult.WarningMessage ??
                        restoreResult.ErrorMessage ??
                        "Unknown restore failure.");
                }

                return new RunAsAppEntryPersistenceResult(
                    RunAsAppEntryPersistenceStatus.SaveFailed,
                    newApp,
                    restoreFailures.Count == 0
                        ? mutationOrSaveEx.Message
                        : $"{mutationOrSaveEx.Message}{Environment.NewLine}{Environment.NewLine}" +
                          $"Restoring the previous application state also failed: {string.Join("; ", restoreFailures)}");
            }
            catch (Exception restoreException)
            {
                log.Error("AppEditCommitService: failed to restore previous app state after save failure", restoreException);
                return new RunAsAppEntryPersistenceResult(
                    RunAsAppEntryPersistenceStatus.SaveFailed,
                    newApp,
                    $"{mutationOrSaveEx.Message}{Environment.NewLine}{Environment.NewLine}" +
                    $"Restoring the previous application state also failed: {restoreException.Message}");
            }
        }

        var applyResult = appEntryManager.ApplyAppChanges(newApp, changeSet);
        if (applyResult.Status != RunAsAppEntryPersistenceStatus.Succeeded)
        {
            NotifyDataChangedBestEffort();
            return applyResult;
        }

        NotifyDataChangedBestEffort();

        return new RunAsAppEntryPersistenceResult(
            RunAsAppEntryPersistenceStatus.Succeeded,
            newApp);
    }

    private void NotifyDataChangedBestEffort()
    {
        try
        {
            dataChangeNotifier.NotifyDataChanged();
        }
        catch (Exception ex)
        {
            log.Warn($"AppEditCommitService: failed to refresh UI: {ex.Message}");
        }
    }

    private void SaveForScope(AppEditConfigSaveScope configSaveScope, string appId)
    {
        if (configSaveScope == AppEditConfigSaveScope.AllConfigs)
        {
            appConfigService.SaveAllConfigs(
                appState.Database,
                session.PinDerivedKey,
                session.CredentialStore.ArgonSalt);
            return;
        }

        appConfigService.SaveConfigForApp(
            appId,
            appState.Database,
            session.PinDerivedKey,
            session.CredentialStore.ArgonSalt);
    }
}
