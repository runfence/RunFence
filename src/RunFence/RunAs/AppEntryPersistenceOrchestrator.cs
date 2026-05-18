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
    /// Persists a new AppEntry: adds to database, saves config, applies RunAs enforcement,
    /// and notifies the UI.
    /// </summary>
    public RunAsAppEntryPersistenceResult PersistNewAppEntry(AppEntry app, string? configPath)
    {
        if (!licenseService.CanAddApp(appState.Database.Apps.Count))
        {
            var message = licenseService.GetRestrictionMessage(EvaluationFeature.Apps, appState.Database.Apps.Count);
            uiThreadInvoker.BeginInvoke(() =>
                MessageBox.Show(message,
                    "License Limit", MessageBoxButtons.OK, MessageBoxIcon.Information));
            return new RunAsAppEntryPersistenceResult(
                RunAsAppEntryPersistenceStatus.Canceled,
                null,
                message);
        }

        appState.Database.Apps.Add(app);
        if (configPath != null)
            appConfigService.AssignApp(app.Id, configPath);

        try
        {
            appConfigService.SaveConfigForApp(
                app.Id,
                appState.Database,
                session.PinDerivedKey,
                session.CredentialStore.ArgonSalt);
        }
        catch (Exception ex)
        {
            log.Error("Failed to create RunAs app entry", ex);
            return new RunAsAppEntryPersistenceResult(
                RunAsAppEntryPersistenceStatus.SaveFailed,
                app,
                ex.Message);
        }

        if (app.RestrictAcl)
        {
            try
            {
                aclService.ApplyAcl(app, appState.Database.Apps);
            }
            catch (Exception ex)
            {
                var status = app.AclMode == AclMode.Deny
                    ? RunAsAppEntryPersistenceStatus.ConvenienceEnforcementFailed
                    : RunAsAppEntryPersistenceStatus.RequiredEnforcementFailed;
                log.Error("RunAs ACL enforcement failed for new app entry", ex);
                NotifyDataChangedBestEffort();
                return new RunAsAppEntryPersistenceResult(
                    status,
                    app,
                    WarningMessage: ex.Message);
            }
        }

        try
        {
            aclService.RecomputeAllAncestorAcls(appState.Database.Apps);
        }
        catch (Exception ex)
        {
            log.Error("RunAs ancestor ACL recompute failed for new app entry", ex);
            NotifyDataChangedBestEffort();
            return new RunAsAppEntryPersistenceResult(
                RunAsAppEntryPersistenceStatus.RequiredEnforcementFailed,
                app,
                WarningMessage: ex.Message);
        }

        try
        {
            if (app.ManageShortcuts)
                shortcutCreator.CreateBesideTargetShortcut(app);
        }
        catch (Exception ex)
        {
            log.Error("Convenience RunAs shortcut enforcement failed for new app entry", ex);
            NotifyDataChangedBestEffort();
            return new RunAsAppEntryPersistenceResult(
                RunAsAppEntryPersistenceStatus.ConvenienceEnforcementFailed,
                app,
                WarningMessage: ex.Message);
        }

        NotifyDataChangedBestEffort();

        return new RunAsAppEntryPersistenceResult(
            RunAsAppEntryPersistenceStatus.Succeeded,
            app);
    }

    private void NotifyDataChangedBestEffort()
    {
        try
        {
            dataChangeNotifier.NotifyDataChanged();
        }
        catch (Exception ex)
        {
            log.Warn($"Failed to refresh UI after RunAs app creation: {ex.Message}");
        }
    }
}
