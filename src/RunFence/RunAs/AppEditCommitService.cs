using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;

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
    public RunAsAppEntryPersistenceResult Commit(AppEntry newApp, AppEntry? previousApp, string? configPath)
    {
        try
        {
            if (previousApp != null)
            {
                return CommitExistingApp(newApp, previousApp, configPath);
            }

            return persistenceOrchestrator.PersistNewAppEntry(newApp, configPath);
        }
        catch (Exception ex)
        {
            log.Error("AppEditCommitService: commit failed", ex);
            return new RunAsAppEntryPersistenceResult(
                RunAsAppEntryPersistenceStatus.SaveFailed,
                newApp,
                ex.Message);
        }
    }

    private RunAsAppEntryPersistenceResult CommitExistingApp(AppEntry newApp, AppEntry previousApp, string? configPath)
    {
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

        var originalConfigPath = appConfigService.GetConfigPath(previousApp.Id);
        var cleanupResult = appEntryManager.RevertAppChanges(previousApp);
        if (cleanupResult.Status != RunAsAppEntryPersistenceStatus.Succeeded)
            return cleanupResult;

        try
        {
            appState.Database.Apps[index] = newApp;
            appConfigService.AssignApp(newApp.Id, configPath);
            appConfigService.SaveAllConfigs(
                appState.Database,
                session.PinDerivedKey,
                session.CredentialStore.ArgonSalt);
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
                    appConfigService.SaveAllConfigs(
                        appState.Database,
                        session.PinDerivedKey,
                        session.CredentialStore.ArgonSalt);
                }
                catch (Exception restoreSaveException)
                {
                    log.Error("AppEditCommitService: failed to restore previous persisted app state after save failure",
                        restoreSaveException);
                    restoreFailures.Add(restoreSaveException.Message);
                }

                var restoreResult = appEntryManager.ApplyAppChanges(previousApp);
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

        var applyResult = appEntryManager.ApplyAppChanges(newApp);
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
}
