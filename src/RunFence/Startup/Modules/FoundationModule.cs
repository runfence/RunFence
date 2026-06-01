using Autofac;
using RunFence.Core;
using RunFence.Core.Ipc;
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
        builder.RegisterType<MachineIdentityReader>().As<IMachineIdentityReader>().SingleInstance();
        builder.RegisterType<MachineIdProvider>().As<IMachineIdProvider>().SingleInstance();
        builder.RegisterType<NTTranslateApi>().SingleInstance();
        builder.RegisterType<GroupMembershipApi>().SingleInstance();
        builder.RegisterType<NativeDpapiProtector>().As<IDpapiProtector>().SingleInstance();
        builder.RegisterType<CredentialEncryptionService>()
            .As<ICredentialEncryptionSpanService>()
            .SingleInstance();
        builder.RegisterType<PinService>().As<IPinService>().SingleInstance();
        builder.RegisterType<ConfigMismatchPinVerifier>().SingleInstance();
        builder.RegisterType<ProductionConfigPaths>().As<IConfigPaths>().SingleInstance();
        builder.RegisterType<PersistenceFileSecurityMirror>().As<IPersistenceFileSecurityMirror>().SingleInstance();
        builder.RegisterType<PersistenceAtomicFileWriter>().As<IPersistenceAtomicFileWriter>().SingleInstance();
        builder.RegisterType<LoadedGoodBackupStore>().As<ILoadedGoodBackupStore>().SingleInstance();
        builder.RegisterType<ManagedPersistenceFileCleaner>().As<IManagedPersistenceFileCleaner>().SingleInstance();
        builder.RegisterType<AppIdValidator>().AsSelf().SingleInstance();
        builder.RegisterType<DatabaseService>()
            .WithParameter(
                (parameterInfo, _) => parameterInfo.Name == "appFilter",
                (_, context) => context.ResolveOptional<IAppFilter>())
            .WithParameter("allowPlaintextConfig", false)
            .As<IDatabaseService>()
            .As<IConfigRepository>()
            .As<ICredentialRepository>()
            .As<ICredentialStorePersistence>()
            .As<IMainConfigPersistence>()
            .As<IAppConfigPersistence>()
            .As<IConfigIntegrityVerifier>()
            .As<IConfigSaltReader>()
            .As<IConfigReencryptionPersistence>()
            .InstancePerLifetimeScope();
        builder.RegisterType<SidResolver>().As<ISidResolver>().SingleInstance();
        builder.RegisterType<ProfilePathResolver>().As<IProfilePathResolver>().SingleInstance();
        builder.RegisterType<ProgramFilesPathProvider>().As<IProgramFilesPathProvider>().SingleInstance();
        builder.RegisterType<InteractiveUserSidResolver>().As<IInteractiveUserSidResolver>().SingleInstance();
        builder.RegisterType<FileSystemExecutableFileSystem>().As<IExecutableFileSystem>().SingleInstance();
        builder.RegisterType<RegistryProfilePathReader>().As<IProfilePathReader>().SingleInstance();
        builder.RegisterType<AppExecLinkReader>().As<IAppExecLinkReader>().SingleInstance();
        builder.RegisterType<WindowsAppsAliasPathResolver>().As<IWindowsAppsAliasPathResolver>().SingleInstance();
        builder.RegisterType<WindowsAppsPackageIdentityResolver>().As<IWindowsAppsPackageIdentityResolver>().SingleInstance();
        builder.RegisterType<ExecutablePathResolver>().As<IExecutablePathResolver>().SingleInstance();
        builder.RegisterType<ExecutableKindService>().As<IExecutableKindService>().SingleInstance();
        builder.RegisterType<AppInitializationHelper>().As<IAppInitializationHelper>().SingleInstance();
        builder.RegisterType<SecureDesktopNative>().As<ISecureDesktopNative>().SingleInstance();
        builder.RegisterType<SecureDesktopHelper>().As<ISecureDesktopRunner>().SingleInstance();
        builder.RegisterType<ModalCoordinator>().As<IModalCoordinator>().SingleInstance();
        builder.RegisterType<PinResetFlowRunner>().As<IPinResetFlowRunner>().SingleInstance();
        builder.RegisterType<StartupUI>().As<IStartupUI>().SingleInstance();
        builder.RegisterType<DefaultIpcClient>().As<IIpcClient>().InstancePerDependency();
        builder.RegisterType<SessionProvider>()
            .As<ISessionProvider>().As<IDatabaseProvider>().AsSelf().SingleInstance();
        builder.RegisterType<SingleInstanceService>().As<ISingleInstanceService>().InstancePerDependency();
        builder.RegisterType<RunningInstanceSidProvider>().As<IRunningInstanceSidProvider>().InstancePerDependency();
        builder.RegisterType<SessionAcquisitionHandler>()
            .AsSelf()
            .InstancePerDependency();

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
