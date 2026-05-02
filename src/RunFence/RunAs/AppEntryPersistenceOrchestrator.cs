using RunFence.Acl;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.Persistence;

namespace RunFence.RunAs;

/// <summary>
/// Persists a new <see cref="AppEntry"/> for the RunAs flow: enforces license limits,
/// adds the app to the database, saves config, applies ACL and shortcuts, and notifies the UI.
/// Rolls back all changes if any step fails.
/// </summary>
public class AppEntryPersistenceOrchestrator(
    IAppStateProvider appState,
    IUiThreadInvoker uiThreadInvoker,
    SessionContext session,
    IAppConfigService appConfigService,
    IAclService aclService,
    IDataChangeNotifier dataChangeNotifier,
    ILicenseService licenseService,
    ILoggingService log,
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
}
