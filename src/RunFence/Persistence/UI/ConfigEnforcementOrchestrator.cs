using RunFence.Acl;
using RunFence.Apps;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Persistence.UI;

/// <summary>
/// Exposes enforcement operations for the loaded-apps lifecycle and the PIN reset flow in
/// <see cref="RunFence.Startup.UI.LockManager"/>.
/// </summary>
public interface ILoadedAppsCleanup
{
    void ApplyLoadedAppsEnforcement(IReadOnlyList<AppEntry> loadedApps);
    void RevertApps(IEnumerable<AppEntry> apps);
    void RecomputeAllAncestorAcls(IReadOnlyList<AppEntry> allApps);

    /// <summary>
    /// Reverts all apps in the current database for shutdown — passes each app the full
    /// database.Apps list as context, then recomputes ancestor ACLs with an empty list.
    /// Per-app errors are logged and silently skipped.
    /// </summary>
    void RevertAllAppsForShutdown();
}

/// <summary>
/// Handles enforcement operations for loaded/unloaded app configs:
/// applies ACLs and shortcuts when loading, and reverts them when unloading.
/// Shutdown-specific cleanup (firewall, context menu, associations, handlers) is in
/// <see cref="ShutdownCleanupService"/>.
/// </summary>
public class ConfigEnforcementOrchestrator(
    ISessionProvider sessionProvider,
    IAclService aclService,
    IIconService iconService,
    ILoggingService log,
    AppEntryEnforcementCoordinator enforcementCoordinator,
    IShortcutDiscoveryService shortcutDiscovery) : ILoadedAppsCleanup
{
    public void ApplyLoadedAppsEnforcement(IReadOnlyList<AppEntry> loadedApps)
    {
        var database = sessionProvider.GetSession().Database;
        var shortcutCache = shortcutDiscovery.CreateTraversalCacheIfNeeded(loadedApps);
        foreach (var app in loadedApps)
        {
            try
            {
                enforcementCoordinator.ApplyChanges(app, database.Apps, shortcutCache);
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
        var shortcutCache = shortcutDiscovery.CreateTraversalCacheIfNeeded(appList);
        foreach (var app in appList)
        {
            try
            {
                enforcementCoordinator.RevertChanges(app, remainingApps.Append(app).ToList(), shortcutCache);
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

    public void RevertAllAppsForShutdown()
    {
        var database = sessionProvider.GetSession().Database;
        var shortcutCache = shortcutDiscovery.CreateTraversalCacheIfNeeded(database.Apps);
        foreach (var app in database.Apps.ToList())
        {
            try
            {
                enforcementCoordinator.RevertChanges(app, database.Apps, shortcutCache);
            }
            catch (Exception ex)
            {
                log.Error($"Cleanup failed for {app.Name}", ex);
            }
        }

        aclService.RecomputeAllAncestorAcls([]);
    }
}
