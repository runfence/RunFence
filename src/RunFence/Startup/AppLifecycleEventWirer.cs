using RunFence.Account.Lifecycle;
using RunFence.DragBridge;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.Persistence.UI;
using RunFence.Startup.UI;
using RunFence.UI.Forms;

namespace RunFence.Startup;

/// <summary>
/// Wires all inter-service event subscriptions during application startup.
/// Extracted from AppLifecycleStarter to give each concern a clear owner.
/// </summary>
public class AppLifecycleEventWirer(
    MainForm mainForm,
    ApplicationState applicationState,
    LockManager lockManager,
    ConfigManagementOrchestrator configManagementOrchestrator,
    ILicenseService licenseService,
    EphemeralAccountService ephemeralAccountService,
    EphemeralContainerService ephemeralContainerService,
    NotifyIcon notifyIcon,
    ISessionProvider sessionProvider,
    OptionsPanel optionsPanel,
    IDragBridgeService dragBridgeService)
{
    public void WireEvents()
    {
        // Ephemeral service events — wired before background services start so no events are missed
        ephemeralAccountService.AccountsChanged +=
            () => mainForm.BeginInvoke(() => applicationState.NotifyDataChanged());
        ephemeralContainerService.ContainersChanged +=
            () => mainForm.BeginInvoke(() => applicationState.NotifyDataChanged());

        // License, config management, and lock manager events
        licenseService.LicenseStatusChanged +=
            () => mainForm.BeginInvoke(() => notifyIcon.Text = licenseService.IsLicensed ? "RunFence" : "RunFence (Evaluation)");
        configManagementOrchestrator.DataRefreshRequested +=
            () => mainForm.BeginInvoke(() => mainForm.SetData());
        configManagementOrchestrator.TrayUpdateRequested +=
            () => mainForm.BeginInvoke(() => mainForm.UpdateTray());
        lockManager.ShowWindowRequested +=
            () => mainForm.BeginInvoke(() => mainForm.ShowWindowNormal());
        lockManager.ShowWindowUnlockedRequested +=
            () => mainForm.BeginInvoke(() => mainForm.ShowWindowUnlocked());
        lockManager.WindowsHelloUnavailableConfirmRequested +=
            () => mainForm.Invoke(() => mainForm.ConfirmWindowsHelloUnavailableFallback());
        lockManager.WindowsHelloFailedConfirmRequested +=
            () => mainForm.Invoke(() => mainForm.ConfirmWindowsHelloFailedFallback());
        applicationState.DataChanged +=
            () => mainForm.BeginInvoke(() => mainForm.HandleDataChanged());

        // DragBridge lifecycle — SetData on data changes, ApplySettings on drag bridge settings change,
        // Dispose on form closed (after OnFormClosing cleanup completes). This removes IDragBridgeService
        // from MainForm's constructor.
        applicationState.DataChanged +=
            () => mainForm.BeginInvoke(() => dragBridgeService.SetData(sessionProvider.GetSession()));
        optionsPanel.DragBridgeSettingsChanged +=
            () => dragBridgeService.ApplySettings(sessionProvider.GetSession().Database.Settings);
        mainForm.FormClosed +=
            (_, _) => dragBridgeService.Dispose();
    }
}