using Autofac;
using Autofac.Extras.Ordering;
using RunFence.Infrastructure;
using RunFence.Security;
using RunFence.Startup;
using RunFence.Startup.UI;

namespace RunFence.Startup.Modules;

public class SecurityModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<StartupSecurityService>()
            .As<IStartupSecurityService>()
            .SingleInstance();

        builder.RegisterType<WindowsHelloService>()
            .As<IWindowsHelloService>()
            .SingleInstance();

        builder.RegisterType<AutoLockTimerService>()
            .As<IAutoLockTimerService>()
            .SingleInstance();

        builder.RegisterType<UnlockProcessLauncher>()
            .As<IUnlockProcessLauncher>()
            .SingleInstance();

        builder.RegisterType<LockManager>()
            .As<ILockManager>()
            .As<ILockUiEventSource>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<ApplicationState>()
            .As<IAppStateProvider>()
            .As<IAppLockControl>()
            .As<IDataChangeNotifier>()
            .As<IApplicationDataChangeSource>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<ShortcutEnforcementHelper>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<StartupEnforcementService>()
            .As<IStartupEnforcementService>()
            .SingleInstance();

        builder.RegisterType<PinChangeOrchestrator>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<FindingLocationHelper>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<SecurityCheckRunner>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<InputInjectionBlockerService>()
            .As<IInputInjectionBlockerService>()
            .As<IRequiresInitialization>()
            .OrderBy(4)
            .SingleInstance();
    }
}
