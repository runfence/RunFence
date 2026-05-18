using Autofac;
using RunFence.Acl;
using RunFence.Apps;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.Persistence.UI;
using RunFence.PrefTrans;
using RunFence.Startup;
using RunFence.Startup.NonElevatedMocks;

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

        builder.RegisterType<GrantIntentOwnershipProjectionService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AppConfigIndex>()
            .As<IAppFilter>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AppConfigSaveHelper>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<HandlerMappingService>()
            .As<IHandlerMappingService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AppConfigService>()
            .As<IAppConfigService>()
            .SingleInstance();

        builder.RegisterType<MainGrantIntentStore>()
            .As<IGrantIntentStore>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<GrantIntentStoreProvider>()
            .As<IGrantIntentStoreProvider>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<GrantIntentRepository>()
            .As<IGrantIntentRepository>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<ConfigGrantPinHelper>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<ConfigLoadUnloadService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<ShutdownCleanupService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<ConfigManagementOrchestrator>()
            .As<IConfigManagementContext>()
            .As<IAdditionalConfigLoadService>()
            .As<IConfigAvailabilityChecker>()
            .As<IConfigManagementEventSource>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<ConfigEnforcementOrchestrator>()
            .As<ILoadedAppsCleanup>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<PrefTransLauncher>()
            .As<IPrefTransLauncher>()
            .SingleInstance();

        builder.RegisterType<SettingsTransferAccessGrantService>()
            .As<ISettingsTransferAccessGrantService>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<SettingsTransferStagingService>()
            .As<ISettingsTransferStagingService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<SettingsTransferService>()
            .As<ISettingsTransferService>()
            .SingleInstance()
            .WithParameter(new NamedParameter("baseDirectory", AppContext.BaseDirectory));

        builder.RegisterType<AppHandlerRegistrationService>()
            .As<IAppHandlerRegistrationService>()
            .SingleInstance();

        builder.RegisterType<AppEntryEnforcementHelper>().AsSelf().SingleInstance();

        builder.RegisterType<ConfigImportFileParser>().As<IConfigImportFileParser>().SingleInstance();
        builder.RegisterType<MainConfigImportPreservationCollector>().AsSelf().SingleInstance();
        builder.RegisterType<MainConfigImportEvaluationValidator>().AsSelf().SingleInstance();
        builder.RegisterType<MainConfigImportRepairService>().AsSelf().SingleInstance();
        builder.RegisterType<MainConfigImportApplyService>().AsSelf().SingleInstance();
        builder.RegisterType<ConfigImportHandler>()
            .AsSelf()
            .SingleInstance();
        builder.RegisterType<AdditionalConfigImportCoordinator>()
            .AsSelf()
            .SingleInstance();

        if (DebugHelper.UseAdminOperationMocks)
            builder.RegisterDecorator<NoOpAppHandlerRegistrationService, IAppHandlerRegistrationService>();
    }
}
