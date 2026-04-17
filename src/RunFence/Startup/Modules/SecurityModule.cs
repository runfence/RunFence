using Autofac;
using RunFence.Infrastructure;
using RunFence.Security;
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

        builder.RegisterType<LockManager>()
            .As<ILockManager>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<ApplicationState>()
            .As<IAppStateProvider>()
            .As<IAppLockControl>()
            .As<IDataChangeNotifier>()
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
    }
}