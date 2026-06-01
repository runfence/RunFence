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
    AppEntryEnforcementCoordinator enforcementCoordinator,
    IShortcutDiscoveryService shortcutDiscovery)
{
    public RunAsAppEntryPersistenceResult RevertAppChanges(AppEntry app, AppEntryChangeSet changeSet)
    {
        var shortcutCache = CreateShortcutCacheIfNeeded(app);
        var cleanupResult = enforcementCoordinator.RevertRunAsChanges(app, appState.Database.Apps, shortcutCache, changeSet);
        if (cleanupResult.Succeeded)
            return new RunAsAppEntryPersistenceResult(RunAsAppEntryPersistenceStatus.Succeeded, app);

        var cleanupMessage = cleanupResult.Message ?? "Unknown cleanup failure.";
        log.Error(
            $"Failed to revert changes for {app.Name}",
            cleanupResult.Exception ?? new InvalidOperationException(cleanupMessage));

        var restoreResult = ApplyAppChanges(app, changeSet);
        if (restoreResult.Status != RunAsAppEntryPersistenceStatus.Succeeded)
        {
            var restoreMessage = restoreResult.WarningMessage ?? restoreResult.ErrorMessage ?? "Unknown restore failure.";
            log.Error(
                $"Failed to restore previous enforcement for {app.Name} after cleanup failure",
                new InvalidOperationException(restoreMessage));
            return new RunAsAppEntryPersistenceResult(
                RunAsAppEntryPersistenceStatus.SaveFailed,
                app,
                $"{cleanupMessage}{Environment.NewLine}{Environment.NewLine}" +
                $"Restoring the previous application state also failed: {restoreMessage}");
        }

        return new RunAsAppEntryPersistenceResult(
            RunAsAppEntryPersistenceStatus.SaveFailed,
            app,
            cleanupMessage);
    }

    public RunAsAppEntryPersistenceResult ApplyAppChanges(AppEntry app, AppEntryChangeSet changeSet)
    {
        var shortcutCache = CreateShortcutCacheIfNeeded(app);
        var result = enforcementCoordinator.ApplyRunAsChanges(app, appState.Database.Apps, shortcutCache, changeSet);
        if (result.Succeeded)
            return new RunAsAppEntryPersistenceResult(RunAsAppEntryPersistenceStatus.Succeeded, app);

        var status = result.FailureKind switch
        {
            AppEntryEnforcementCoordinator.EnforcementFailureKind.Convenience => RunAsAppEntryPersistenceStatus.ConvenienceEnforcementFailed,
            AppEntryEnforcementCoordinator.EnforcementFailureKind.Required => RunAsAppEntryPersistenceStatus.RequiredEnforcementFailed,
            AppEntryEnforcementCoordinator.EnforcementFailureKind.Cleanup => RunAsAppEntryPersistenceStatus.SaveFailed,
            _ => RunAsAppEntryPersistenceStatus.SaveFailed
        };

        return new RunAsAppEntryPersistenceResult(
            status,
            app,
            WarningMessage: result.Message);
    }

    private ShortcutTraversalCache CreateShortcutCacheIfNeeded(AppEntry app)
        => app.ManageShortcuts
            ? shortcutDiscovery.CreateTraversalCache()
            : new ShortcutTraversalCache([]);

}
