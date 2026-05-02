using Autofac;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.Launching.Resolution;
using RunFence.Persistence;
using RunFence.Security;
using RunFence.Startup.NonElevatedMocks;
using RunFence.Startup.UI;

namespace RunFence.Startup.Modules;

public class FoundationModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<LoggingService>().As<ILoggingService>().SingleInstance();
        builder.RegisterType<MachineIdProvider>().As<IMachineIdProvider>().SingleInstance();
        builder.RegisterType<NTTranslateApi>().SingleInstance();
        builder.RegisterType<GroupMembershipApi>().SingleInstance();
        builder.RegisterType<CredentialEncryptionService>().As<ICredentialEncryptionService>().SingleInstance();
        builder.RegisterType<PinService>().As<IPinService>().SingleInstance();
        builder.RegisterType<ProductionConfigPaths>().As<IConfigPaths>().SingleInstance();
        builder.RegisterType<AppConfigIndex>().As<IAppFilter>().AsSelf().SingleInstance();
        builder.RegisterType<AppConfigSaveHelper>().AsSelf().SingleInstance();
        builder.RegisterType<AppConfigService>().As<IAppConfigService>().SingleInstance();
        builder.Register(c => new DatabaseService(
                c.Resolve<ILoggingService>(),
                c.Resolve<IConfigPaths>(),
                c.ResolveOptional<IAppFilter>(),
                allowPlaintextConfig: false))
            .As<IDatabaseService>().As<IConfigRepository>().As<ICredentialRepository>().SingleInstance();
        builder.RegisterType<GrantConfigTracker>().As<IGrantConfigTracker>().AsSelf().SingleInstance();
        builder.RegisterType<HandlerMappingService>().As<IHandlerMappingService>().AsSelf().SingleInstance();
        builder.RegisterType<SidResolver>().As<ISidResolver>().SingleInstance();
        builder.RegisterType<ProfilePathResolver>().As<IProfilePathResolver>().SingleInstance();
        builder.RegisterType<InteractiveUserSidResolver>().As<IInteractiveUserSidResolver>().SingleInstance();
        builder.RegisterType<FileSystemExecutableFileSystem>().As<IExecutableFileSystem>().SingleInstance();
        builder.RegisterType<RegistryProfilePathReader>().As<IProfilePathReader>().SingleInstance();
        builder.RegisterType<ExecutablePathResolver>().As<IExecutablePathResolver>().SingleInstance();
        builder.RegisterType<ExecutableKindService>().As<IExecutableKindService>().SingleInstance();
        builder.RegisterType<AppInitializationHelper>().As<IAppInitializationHelper>().SingleInstance();
        builder.RegisterType<SecureDesktopHelper>().As<ISecureDesktopRunner>().SingleInstance();
        builder.RegisterType<ModalCoordinator>().As<IModalCoordinator>().SingleInstance();
        builder.RegisterType<PinResetFlowRunner>().As<IPinResetFlowRunner>().SingleInstance();
        builder.RegisterType<StartupUI>().As<IStartupUI>().SingleInstance();
        builder.RegisterType<SessionProvider>()
            .As<ISessionProvider>().As<IDatabaseProvider>().AsSelf().SingleInstance();

        builder.RegisterType<SidDisplayNameResolver>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<TpmNativeApiAdapter>().As<ITpmNativeApi>().SingleInstance();
        builder.RegisterType<TpmHandleProvider>().SingleInstance();
        builder.RegisterType<TpmPublicKeyExporter>().SingleInstance();
        builder.RegisterType<TpmKeyCreationPolicy>().SingleInstance();
        builder.RegisterType<TpmDecryptPolicy>().SingleInstance();
        builder.RegisterType<TpmKeyProvider>().As<ITpmKeyProvider>().SingleInstance();
        builder.RegisterType<RememberPinService>().As<IRememberPinService>().SingleInstance();

        builder.RegisterType<UserHiveManager>().As<IUserHiveManager>().SingleInstance();
        builder.RegisterType<UserImpersonationHelper>().As<IUserImpersonationHelper>().SingleInstance();

        builder.RegisterType<StartupCredentialLoader>().As<IStartupCredentialLoader>().AsSelf().SingleInstance();
        builder.RegisterType<StartupSessionScopeFactory>().As<IStartupSessionScopeFactory>().SingleInstance();
        builder.RegisterType<StartupMainFormRunner>().As<IStartupMainFormRunner>().SingleInstance();
        builder.RegisterType<StartupOrchestrator>().AsSelf().SingleInstance();

        if (DebugHelper.UseAdminOperationMocks)
            builder.RegisterDecorator<NoOpUserHiveManager, IUserHiveManager>();
    }
}
