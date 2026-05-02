using RunFence.Acl;
using RunFence.Apps;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.RunAs;

/// <summary>
/// Manages app entry enforcement and shortcut operations for the RunAs flow.
/// Persistence of new entries is handled by <see cref="AppEntryPersistenceOrchestrator"/>.
/// Dialog display is handled by <see cref="RunAsAppEditDialogHandler"/>.
/// </summary>
public class RunAsAppEntryManager(
    IAppStateProvider appState,
    ILoggingService log,
    IAclService aclService,
    AppEntryEnforcementHelper enforcementHelper,
    IShortcutDiscoveryService shortcutDiscovery)
{
    public void RevertAppChanges(AppEntry app)
    {
        try
        {
            var shortcutCache = CreateShortcutCacheIfNeeded(app);
            enforcementHelper.RevertChanges(app, appState.Database.Apps, shortcutCache);
            var appsAfterRevert = appState.Database.Apps.Where(a => a.Id != app.Id).ToList();
            aclService.RecomputeAllAncestorAcls(appsAfterRevert);
        }
        catch (Exception ex)
        {
            log.Error($"Failed to revert changes for {app.Name}", ex);
        }
    }

    public void ApplyAppChanges(AppEntry app)
    {
        var shortcutCache = CreateShortcutCacheIfNeeded(app);
        enforcementHelper.ApplyChanges(app, appState.Database.Apps, shortcutCache);
        aclService.RecomputeAllAncestorAcls(appState.Database.Apps);
    }

    private ShortcutTraversalCache CreateShortcutCacheIfNeeded(AppEntry app)
        => app.ManageShortcuts
            ? shortcutDiscovery.CreateTraversalCache()
            : new ShortcutTraversalCache([]);
}
