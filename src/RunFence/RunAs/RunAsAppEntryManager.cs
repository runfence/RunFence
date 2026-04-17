using RunFence.Acl;
using RunFence.Apps;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.Persistence;

namespace RunFence.RunAs;

/// <summary>
/// Manages app entry persistence, enforcement, and shortcut creation for the RunAs flow.
/// Dialog display is handled by <see cref="RunAsAppEditDialogHandler"/>.
/// </summary>
public class RunAsAppEntryManager(
    IAppStateProvider appState,
    IUiThreadInvoker uiThreadInvoker,
    IDataChangeNotifier dataChangeNotifier,
    ILoggingService log,
    SessionContext session,
    IAppConfigService appConfigService,
    IAclService aclService,
    AppEntryEnforcementHelper enforcementHelper,
    IShortcutDiscoveryService shortcutDiscovery,
    ILicenseService licenseService,
    RunAsAppShortcutCreator shortcutCreator)
{
    /// <summary>
    /// Persists a new AppEntry: adds to database, saves config, applies ACL/shortcuts,
    /// and notifies the UI. Returns true on success; removes the app from the database on failure.
    /// </summary>
    public bool PersistNewAppEntry(AppEntry app, string? configPath)
    {
        if (!licenseService.CanAddApp(appState.Database.Apps.Count))
        {
            uiThreadInvoker.BeginInvoke(() =>
                MessageBox.Show(licenseService.GetRestrictionMessage(EvaluationFeature.Apps, appState.Database.Apps.Count),
                    "License Limit", MessageBoxButtons.OK, MessageBoxIcon.Information));
            return false;
        }

        try
        {
            appState.Database.Apps.Add(app);

            if (configPath != null)
                appConfigService.AssignApp(app.Id, configPath);

            using var scope = session.PinDerivedKey.Unprotect();
            appConfigService.SaveConfigForApp(app.Id, appState.Database,
                scope.Data, session.CredentialStore.ArgonSalt);

            if (app.RestrictAcl)
            {
                try
                {
                    aclService.ApplyAcl(app, appState.Database.Apps);
                }
                catch (Exception ex)
                {
                    log.Error("Failed to apply ACL for RunAs app", ex);
                }
            }

            if (app.ManageShortcuts)
                shortcutCreator.CreateBesideTargetShortcut(app);

            aclService.RecomputeAllAncestorAcls(appState.Database.Apps);
        }
        catch (Exception ex)
        {
            log.Error("Failed to create RunAs app entry", ex);
            // Revert ACLs/shortcuts before removing app (app must still be in allApps for RevertAcl)
            if (app.RestrictAcl)
                try
                {
                    aclService.RevertAcl(app, appState.Database.Apps);
                }
                catch
                {
                }

            if (app.ManageShortcuts)
                shortcutCreator.RemoveBesideTargetShortcut(app);

            appState.Database.Apps.Remove(app);
            try
            {
                aclService.RecomputeAllAncestorAcls(appState.Database.Apps);
            }
            catch
            {
            }

            if (configPath != null)
                appConfigService.RemoveApp(app.Id);
            return false;
        }

        try
        {
            dataChangeNotifier.NotifyDataChanged();
        }
        catch (Exception ex)
        {
            log.Warn($"Failed to refresh UI after RunAs app creation: {ex.Message}");
        }

        return true;
    }

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
