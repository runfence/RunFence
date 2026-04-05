using Autofac;
using RunFence.Apps;
using RunFence.Persistence.UI;
using RunFence.PrefTrans;

namespace RunFence.Startup.Modules;

public class PersistenceModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<ConfigMismatchKeyResolver>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<HandlerSyncHelper>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<ConfigManagementOrchestrator>()
            .As<IConfigManagementContext>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<ConfigEnforcementOrchestrator>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<PrefTransLauncher>()
            .As<IPrefTransLauncher>()
            .SingleInstance();

        builder.RegisterType<SettingsTransferService>()
            .As<ISettingsTransferService>()
            .SingleInstance();

        builder.RegisterType<AppHandlerRegistrationService>()
            .As<IAppHandlerRegistrationService>()
            .SingleInstance();
    }
}