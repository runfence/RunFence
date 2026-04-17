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
    IAppStateProvider appState) : IAppEditCommitService
{
    public bool Commit(AppEntry newApp, AppEntry? previousApp, string? configPath)
    {
        try
        {
            if (previousApp != null)
            {
                var index = appState.Database.Apps.FindIndex(a => a.Id == previousApp.Id);
                if (index < 0)
                    return false;

                var originalConfigPath = appConfigService.GetConfigPath(previousApp.Id);
                appEntryManager.RevertAppChanges(previousApp);
                appState.Database.Apps[index] = newApp;
                appConfigService.AssignApp(newApp.Id, configPath);
                try
                {
                    appEntryManager.ApplyAppChanges(newApp);
                    SaveAllConfigsCore();
                }
                catch
                {
                    appState.Database.Apps[index] = previousApp;
                    appConfigService.AssignApp(previousApp.Id, originalConfigPath);
                    try
                    {
                        appEntryManager.ApplyAppChanges(previousApp);
                    }
                    catch (Exception restoreEx)
                    {
                        log.Error("AppEditCommitService: failed to restore ACL after edit failure", restoreEx);
                    }

                    throw;
                }
            }
            else
            {
                if (!appEntryManager.PersistNewAppEntry(newApp, configPath))
                    return false;
            }

            try
            {
                dataChangeNotifier.NotifyDataChanged();
            }
            catch (Exception ex)
            {
                log.Warn($"AppEditCommitService: failed to refresh UI: {ex.Message}");
            }

            return true;
        }
        catch (Exception ex)
        {
            log.Error("AppEditCommitService: commit failed", ex);
            return false;
        }
    }

    public void Rollback(AppEntry originalApp, string? configPath)
    {
        try
        {
            appConfigService.AssignApp(originalApp.Id, configPath);
            appEntryManager.ApplyAppChanges(originalApp);
        }
        catch (Exception ex)
        {
            log.Error("AppEditCommitService: rollback failed", ex);
        }
    }

    public void SaveAllConfigs()
    {
        try
        {
            SaveAllConfigsCore();
        }
        catch (Exception ex)
        {
            log.Error("AppEditCommitService: SaveAllConfigs failed", ex);
        }
    }

    private void SaveAllConfigsCore()
    {
        using var scope = session.PinDerivedKey.Unprotect();
        appConfigService.SaveAllConfigs(appState.Database, scope.Data, session.CredentialStore.ArgonSalt);
    }
}
