using RunFence.Apps;
using RunFence.Core.Models;
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
    ILicenseService licenseService,
    IEvaluationLimitHelper evaluationLimitHelper)
{
    public void ActivateContextMenus(AppDatabase database)
    {
        if (database.Settings.EnableRunAsContextMenu)
            contextMenuService.Register();
        else
            contextMenuService.Unregister();
    }

    public void SyncHandlerRegistrations(AppDatabase database)
    {
        var effectiveMappings = handlerMappingService.GetEffectiveHandlerMappings(database);
        appHandlerRegistrationService.Sync(effectiveMappings, database.Apps);
    }

    public void LaunchFirstRunWizardIfNeeded(MainForm mainForm)
    {
        if (startupOptions.IsBackground)
            return;
        var session = sessionProvider.GetSession();
        var credCount = evaluationLimitHelper.CountCredentialsExcludingCurrent(session.CredentialStore.Credentials);
        if (session.Database.Apps.Count == 0 && credCount == 0)
            mainForm.Shown += async (_, _) => await wizardLauncher.OpenWizardAsync(mainForm);
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