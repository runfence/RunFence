using RunFence.Apps;
using RunFence.Core;
using RunFence.Firewall;
using RunFence.Infrastructure;
using RunFence.Launch;

namespace RunFence.Persistence.UI;

/// <summary>Result of <see cref="ShutdownCleanupService.CleanupAllApps"/>.</summary>
public enum CleanupAllAppsResult
{
    /// <summary>Cleanup completed and the caller should exit the application.</summary>
    ReadyToExit,

    /// <summary>Cleanup was skipped because an operation was already in progress.</summary>
    OperationInProgress
}

/// <summary>
/// Executes the full application shutdown cleanup sequence: reverts all app enforcement,
/// removes firewall rules, unregisters context menu/handler/association/folder-handler
/// registrations, then signals that the application is ready to exit.
/// Extracted from <see cref="ConfigEnforcementOrchestrator"/> so that the 5 shutdown-specific
/// service dependencies do not inflate the enforcement class.
/// </summary>
public class ShutdownCleanupService(
    ILoadedAppsCleanup loadedAppsCleanup,
    ISessionProvider sessionProvider,
    ILoggingService log,
    IContextMenuService contextMenuService,
    IAppHandlerRegistrationService handlerRegistrationService,
    IAssociationAutoSetService associationAutoSetService,
    IFolderHandlerService folderHandlerService,
    IFirewallCleanupService firewallCleanupService,
    IJobKeeperService jobKeeperService,
    IProcessJobManager processJobManager)
{
    public CleanupAllAppsResult CleanupAllApps(bool isEnforcementInProgress, bool isOperationInProgress)
    {
        if (isEnforcementInProgress || isOperationInProgress)
            return CleanupAllAppsResult.OperationInProgress;

        try
        {
            loadedAppsCleanup.RevertAllAppsForShutdown();
            firewallCleanupService.RemoveAll(sessionProvider.GetSession().Database);
            log.Info("Cleanup complete, exiting");
        }
        catch (Exception ex)
        {
            log.Error("Cleanup failed", ex);
        }

        contextMenuService.Unregister();
        associationAutoSetService.RestoreForAllUsers();
        handlerRegistrationService.UnregisterAll();
        folderHandlerService.UnregisterAll();
        jobKeeperService.TerminateAllJobKeepers();
        return CleanupAllAppsResult.ReadyToExit;
    }

    /// <summary>
    /// Terminates job keepers whose restricted job contains no processes other than the keeper
    /// itself — i.e. all user-launched processes have already exited. Keepers with unknown job
    /// state (handle not yet cached) are left running to avoid terminating active sessions.
    /// </summary>
    public void TerminateEmptyJobKeepers()
    {
        foreach (var (sid, isLow, keeperPid) in jobKeeperService.GetAllKeepers())
        {
            var members = processJobManager.GetKeeperJobMembers(sid, isLow);
            if (members != null && members.All(pid => pid == keeperPid))
                jobKeeperService.TerminateJobKeeper(sid, isLow);
        }
    }
}
