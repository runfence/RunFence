using Autofac;
using RunFence.Apps;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.Persistence.UI;
using RunFence.PrefTrans;

namespace RunFence.Startup.Modules;

public class PersistenceModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<ConfigAvailabilityMonitor>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<ConfigMismatchKeyResolver>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<HandlerSyncHelper>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<ConfigSaveOrchestrator>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<ConfigManagementOrchestrator>()
            .As<IConfigManagementContext>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<ConfigEnforcementOrchestrator>()
            .As<ILoadedAppsCleanup>()
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

        builder.RegisterType<AppEntryEnforcementHelper>().AsSelf().SingleInstance();

        builder.RegisterType<ConfigImportHandler>()
            .AsSelf()
            .SingleInstance();
    }
}