using RunFence.Apps;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.Persistence;
using RunFence.Startup.UI;
using RunFence.UI.Forms;
using RunFence.Wizard.UI;

namespace RunFence.Startup;

/// <summary>
/// Activates feature registrations at startup: context menus, app handler associations,
/// and the first-run wizard.
/// </summary>
public class StartupFeatureActivator(
    IContextMenuService contextMenuService,
    IAppHandlerRegistrationService appHandlerRegistrationService,
    IHandlerMappingService handlerMappingService,
    WizardLauncher wizardLauncher,
    ISessionProvider sessionProvider,
    IStartupOptions startupOptions,
    LockManager lockManager,
    ILicenseService licenseService)
{
    public void ActivateContextMenus()
    {
        var database = sessionProvider.GetSession().Database;
        if (database.Settings.EnableRunAsContextMenu)
            contextMenuService.Register();
        else
            contextMenuService.Unregister();
    }

    public void SyncHandlerRegistrations()
    {
        var database = sessionProvider.GetSession().Database;
        var effectiveMappings = handlerMappingService.GetEffectiveHandlerMappings(database);
        appHandlerRegistrationService.Sync(effectiveMappings, database.Apps);
    }

    public void LaunchFirstRunWizardIfNeeded(MainForm mainForm)
    {
        if (startupOptions.IsBackground)
            return;
        var session = sessionProvider.GetSession();
        var credCount = EvaluationLimitHelper.CountCredentialsExcludingCurrent(session.CredentialStore.Credentials);
        if (session.Database.Apps.Count == 0 && credCount == 0)
            mainForm.Shown += (_, _) => wizardLauncher.OpenWizard(mainForm);
    }

    public void ConfigureBackgroundMode(MainForm mainForm)
    {
        if (!startupOptions.IsBackground)
            return;
        mainForm.SuppressInitialVisibility = true;
        mainForm.WindowState = FormWindowState.Minimized;
        mainForm.ShowInTaskbar = false;
        if (licenseService.IsLicensed)
            lockManager.StartAutoLockTimer();
    }
}