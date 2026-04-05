using RunFence.DragBridge;
using RunFence.Infrastructure;
using RunFence.Persistence.UI;
using RunFence.UI.Forms;

namespace RunFence.Startup;

/// <summary>
/// Handles all post-resolution initialization. Called explicitly from Program.cs via Start().
/// All dependencies are injected via constructor — does NOT take IContainer or ILifetimeScope.
/// Side effects happen only in Start(); constructors are side-effect-free.
/// </summary>
public class AppLifecycleStarter(
    IOrderedEnumerable<IRequiresInitialization> initServices,
    IOrderedEnumerable<IBackgroundService> backgroundServices,
    MainForm mainForm,
    ISessionProvider sessionProvider,
    ConfigManagementOrchestrator configManagementOrchestrator,
    AppLifecycleEventWirer eventWirer,
    StartupIpcBootstrapper ipcBootstrapper,
    StartupFeatureActivator featureActivator,
    IDragBridgeService dragBridgeService,
    DeferredStartupRunner deferredStartupRunner)
{
    public void Start()
    {
        // Phase 1: Fast path — runs before Application.Run, completes quickly.

        // 1. Initialize services in guaranteed order (GlobalHotkeyService → MediaKeyBridgeService → DragBridgeService)
        //    LicenseService (order 0) is already initialized in Program.cs before MainForm resolution
        //    because MainForm's constructor reads license state. Initialize() is idempotent.
        foreach (var service in initServices)
            service.Initialize();

        // 2. One-time startup wiring: idle monitor config, guard owner, drag bridge initial data
        mainForm.ConfigureIdleMonitor();
        configManagementOrchestrator.SetGuardOwner(mainForm.GuardOwner);
        var session = sessionProvider.GetSession();
        dragBridgeService.SetData(session);
        dragBridgeService.ApplySettings(session.Database.Settings);

        // 3. Wire all event subscriptions before starting background services
        eventWirer.WireEvents();

        // 4. Start background services in guaranteed order
        //    (EphemeralAccountService → EphemeralContainerService → FirewallDnsRefreshService)
        foreach (var service in backgroundServices)
            service.Start();

        // 5. Wire MainForm.HandleCreated: start IPC server and firewall enforcement on first handle created
        ipcBootstrapper.SetupIpcOnHandleCreated();

        // 6. First-run wizard: open wizard when no apps and no non-current credentials exist
        featureActivator.LaunchFirstRunWizardIfNeeded(mainForm);

        // 7. Background mode setup
        featureActivator.ConfigureBackgroundMode(mainForm);

        // Phase 2: Deferred — queued via HandleCreated + BeginInvoke, runs after form handle is created.
        // The guard ensures the handler fires only once even if the handle is recreated.
        // Subscribing after SetupIpcOnHandleCreated ensures our BeginInvoke is queued after
        // SetStartupComplete in the message queue.
        bool deferredStarted = false;
        mainForm.HandleCreated += (_, _) =>
        {
            if (deferredStarted)
                return;
            deferredStarted = true;
            mainForm.BeginInvoke(() => deferredStartupRunner.Run(mainForm));
        };
    }
}