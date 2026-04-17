using RunFence.Acl;
using RunFence.Apps;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Firewall;
using RunFence.Infrastructure;
using RunFence.Launch;

namespace RunFence.Persistence.UI;

/// <summary>
/// Exposes enforcement revert operations needed by the PIN reset flow in
/// <see cref="RunFence.Startup.UI.LockManager"/> to clean up disk-side enforcement before
/// discarding the current database.
/// </summary>
public interface ILoadedAppsCleanup
{
    /// <summary>Reverts enforcement (ACLs, shortcuts, icons) for all provided apps.</summary>
    void RevertApps(IEnumerable<AppEntry> apps);

    /// <summary>Recomputes all ancestor ACLs given the complete current app list.</summary>
    void RecomputeAllAncestorAcls(IReadOnlyList<AppEntry> allApps);
}

/// <summary>Result of <see cref="ConfigEnforcementOrchestrator.CleanupAllApps"/>.</summary>
public enum CleanupAllAppsResult
{
    /// <summary>Cleanup completed and the caller should exit the application.</summary>
    ReadyToExit,

    /// <summary>Cleanup was skipped because an operation was already in progress.</summary>
    OperationInProgress
}

/// <summary>
/// Handles enforcement operations for loaded/unloaded app configs:
/// applies ACLs and shortcuts when loading, reverts them when unloading,
/// and performs full cleanup on shutdown.
/// </summary>
public class ConfigEnforcementOrchestrator(
    ISessionProvider sessionProvider,
    IAclService aclService,
    IIconService iconService,
    IContextMenuService contextMenuService,
    ILoggingService log,
    AppEntryEnforcementHelper enforcementHelper,
    IShortcutDiscoveryService shortcutDiscovery,
    IAppHandlerRegistrationService handlerRegistrationService,
    IAssociationAutoSetService associationAutoSetService,
    IFolderHandlerService folderHandlerService,
    IFirewallCleanupService firewallCleanupService) : ILoadedAppsCleanup
{
    public void ApplyLoadedAppsEnforcement(IReadOnlyList<AppEntry> loadedApps)
    {
        var database = sessionProvider.GetSession().Database;
        var shortcutCache = CreateShortcutCacheIfNeeded(loadedApps);
        foreach (var app in loadedApps)
        {
            try
            {
                enforcementHelper.ApplyChanges(app, database.Apps, shortcutCache);
            }
            catch (Exception ex)
            {
                log.Error($"Failed to apply changes for '{app.Name}' during LoadApps", ex);
            }
        }

        aclService.RecomputeAllAncestorAcls(database.Apps);
    }

    public void RevertApps(IEnumerable<AppEntry> apps)
    {
        var appList = apps.ToList();
        var remainingApps = sessionProvider.GetSession().Database.Apps;
        var shortcutCache = CreateShortcutCacheIfNeeded(appList);
        foreach (var app in appList)
        {
            try
            {
                enforcementHelper.RevertChanges(app, remainingApps.Append(app).ToList(), shortcutCache);
                iconService.DeleteIcon(app.Id);
            }
            catch (Exception ex)
            {
                log.Error($"Failed to revert app '{app.Name}'", ex);
            }
        }
    }

    public void RecomputeAllAncestorAcls(IReadOnlyList<AppEntry> allApps)
        => aclService.RecomputeAllAncestorAcls(allApps);

    public CleanupAllAppsResult CleanupAllApps(bool isEnforcementInProgress, bool isOperationInProgress)
    {
        if (isEnforcementInProgress || isOperationInProgress)
            return CleanupAllAppsResult.OperationInProgress;

        try
        {
            var database = sessionProvider.GetSession().Database;
            var shortcutCache = CreateShortcutCacheIfNeeded(database.Apps);
            foreach (var app in database.Apps.ToList())
            {
                try
                {
                    enforcementHelper.RevertChanges(app, database.Apps, shortcutCache);
                }
                catch (Exception ex)
                {
                    log.Error($"Cleanup failed for {app.Name}", ex);
                }
            }

            aclService.RecomputeAllAncestorAcls([]);
            firewallCleanupService.RemoveAll(database);
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
        return CleanupAllAppsResult.ReadyToExit;
    }

    private ShortcutTraversalCache CreateShortcutCacheIfNeeded(IEnumerable<AppEntry> apps)
        => apps.Any(a => a.ManageShortcuts)
            ? shortcutDiscovery.CreateTraversalCache()
            : new ShortcutTraversalCache([]);
}
