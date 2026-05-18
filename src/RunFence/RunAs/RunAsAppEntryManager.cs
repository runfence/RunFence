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
    public RunAsAppEntryPersistenceResult RevertAppChanges(AppEntry app)
    {
        var shortcutCache = CreateShortcutCacheIfNeeded(app);

        try
        {
            if (app is { RestrictAcl: true, IsUrlScheme: false })
                aclService.RevertAcl(app, appState.Database.Apps);
            enforcementHelper.RevertNonAclChanges(app, shortcutCache);
            var appsAfterRevert = appState.Database.Apps.Where(a => a.Id != app.Id).ToList();
            aclService.RecomputeAllAncestorAcls(appsAfterRevert);
        }
        catch (Exception cleanupEx)
        {
            log.Error($"Failed to revert changes for {app.Name}", cleanupEx);

            var restoreResult = ApplyAppChanges(app);
            if (restoreResult.Status != RunAsAppEntryPersistenceStatus.Succeeded)
            {
                log.Error(
                    $"Failed to restore previous enforcement for {app.Name} after cleanup failure",
                    new InvalidOperationException(
                        restoreResult.WarningMessage ??
                        restoreResult.ErrorMessage ??
                        "Unknown restore failure."));
                return new RunAsAppEntryPersistenceResult(
                    RunAsAppEntryPersistenceStatus.SaveFailed,
                    app,
                    $"{cleanupEx.Message}{Environment.NewLine}{Environment.NewLine}" +
                    $"Restoring the previous application state also failed: " +
                    $"{restoreResult.WarningMessage ?? restoreResult.ErrorMessage ?? "Unknown restore failure."}");
            }

            return new RunAsAppEntryPersistenceResult(
                RunAsAppEntryPersistenceStatus.SaveFailed,
                app,
                cleanupEx.Message);
        }

        return new RunAsAppEntryPersistenceResult(
            RunAsAppEntryPersistenceStatus.Succeeded,
            app);
    }

    public RunAsAppEntryPersistenceResult ApplyAppChanges(AppEntry app)
    {
        var shortcutCache = CreateShortcutCacheIfNeeded(app);

        if (app is { RestrictAcl: true, IsUrlScheme: false })
        {
            try
            {
                aclService.ApplyAcl(app, appState.Database.Apps);
            }
            catch (Exception ex)
            {
                return new RunAsAppEntryPersistenceResult(
                    app.AclMode == AclMode.Deny
                        ? RunAsAppEntryPersistenceStatus.ConvenienceEnforcementFailed
                        : RunAsAppEntryPersistenceStatus.RequiredEnforcementFailed,
                    app,
                    WarningMessage: ex.Message);
            }
        }

        try
        {
            enforcementHelper.ApplyNonAclChanges(app, shortcutCache);
        }
        catch (Exception ex)
        {
            return new RunAsAppEntryPersistenceResult(
                RunAsAppEntryPersistenceStatus.ConvenienceEnforcementFailed,
                app,
                WarningMessage: ex.Message);
        }

        try
        {
            aclService.RecomputeAllAncestorAcls(appState.Database.Apps);
        }
        catch (Exception ex)
        {
            return new RunAsAppEntryPersistenceResult(
                RunAsAppEntryPersistenceStatus.RequiredEnforcementFailed,
                app,
                WarningMessage: ex.Message);
        }

        return new RunAsAppEntryPersistenceResult(
            RunAsAppEntryPersistenceStatus.Succeeded,
            app);
    }

    private ShortcutTraversalCache CreateShortcutCacheIfNeeded(AppEntry app)
        => app.ManageShortcuts
            ? shortcutDiscovery.CreateTraversalCache()
            : new ShortcutTraversalCache([]);
}
