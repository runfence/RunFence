using Autofac;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Apps;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Ipc;
using RunFence.Launch;
using RunFence.Launch.Container;
using RunFence.Launch.Tokens;
using RunFence.Launching.Resolution;
using RunFence.Startup.NonElevatedMocks;

namespace RunFence.Startup.Modules;

public class LaunchModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<MessageBoxProfileRepairPrompt>()
            .As<IProfileRepairPrompt>()
            .SingleInstance();
        builder.RegisterType<ProfileRepairHelper>()
            .As<IProfileRepairHelper>()
            .SingleInstance();
        builder.RegisterType<LsaRightsHelper>().As<ILsaRightsHelper>().SingleInstance();
        builder.RegisterType<InteractiveLogonHelper>().As<IInteractiveLogonHelper>().SingleInstance();
        builder.RegisterType<ExplorerTokenProvider>().As<IExplorerTokenProvider>().SingleInstance();
        builder.RegisterType<SystemTokenProvider>().As<ISystemTokenProvider>().SingleInstance();
        builder.RegisterType<LogonTokenProvider>().As<ILogonTokenProvider>().SingleInstance();
        builder.RegisterType<AppContainerEnvironmentSetup>().As<IAppContainerEnvironmentSetup>().SingleInstance();
        builder.RegisterType<AppContainerProfileSetup>().As<IAppContainerProfileSetup>().SingleInstance();
        builder.RegisterType<AppContainerDataFolderService>().As<IAppContainerDataFolderService>().SingleInstance();
        builder.RegisterType<AppContainerComAccessService>().As<IAppContainerComAccessService>().InstancePerDependency();
        builder.RegisterType<AppContainerSidProvider>().As<IAppContainerSidProvider>().SingleInstance();
        builder.RegisterType<AppContainerProcessLauncher>().As<IAppContainerProcessLauncher>().SingleInstance();
        builder.RegisterType<ElevatedLinkedTokenProvider>().As<IElevatedLinkedTokenProvider>().SingleInstance();
        builder.RegisterType<SaferDeElevationHelper>().As<ISaferDeElevationHelper>().SingleInstance();
        builder.RegisterType<RestrictedProcessControl>().As<IRestrictedProcessControl>().SingleInstance();
        builder.RegisterType<RestrictedProcessActivationGuard>().AsSelf().SingleInstance();
        builder.RegisterType<JobKeeperLaunchProcessApi>().As<IJobKeeperLaunchProcessApi>().SingleInstance();
        builder.Register(c => new RestrictedJobLaunchCoordinator(
                c.Resolve<ILoggingService>(),
                c.Resolve<IProcessJobManager>(),
                c.Resolve<IJobKeeperService>(),
                c.Resolve<IJobKeeperIdentityStore>(),
                c.Resolve<IJobKeeperPipeServerFactory>(),
                c.Resolve<IJobKeeperLaunchIpcClient>(),
                c.Resolve<IJobObjectApi>(),
                c.Resolve<RestrictedProcessActivationGuard>(),
                c.Resolve<IJobKeeperLaunchProcessApi>(),
                jobKeeperExePath: Path.Combine(AppContext.BaseDirectory, PathConstants.JobKeeperExeName)))
            .As<IRestrictedJobLaunchCoordinator>()
            .As<IRestrictedJobLaunchCoordinatorInitializer>()
            .AsSelf()
            .SingleInstance();
        builder.RegisterType<AccountProcessLauncher>().As<IAccountProcessLauncher>().SingleInstance();
        builder.Register(c => new CreateProcessLauncherHelper(
                c.Resolve<ILoggingService>(),
                c.Resolve<IElevatedLinkedTokenProvider>(),
                c.Resolve<ISaferDeElevationHelper>(),
                c.Resolve<IProcessJobManager>(),
                c.Resolve<IJobKeeperService>(),
                c.Resolve<IRestrictedJobLaunchCoordinator>(),
                c.Resolve<IExecutablePathResolver>()))
            .As<ICreateProcessLauncherHelper>()
            .OnActivated(e => e.Context.Resolve<IRestrictedJobLaunchCoordinatorInitializer>()
                .Initialize((IRestrictedJobProcessLauncher)e.Instance))
            .SingleInstance();
        builder.RegisterType<AppContainerService>().As<IAppContainerService>().As<IAppContainerProfileService>().SingleInstance();
        builder.RegisterType<ProcessLauncher>().As<IProcessLauncher>().SingleInstance();
        builder.RegisterType<LaunchDefaultsResolver>().As<ILaunchDefaultsResolver>().SingleInstance();
        builder.RegisterType<AppEntryLaunchPlanBuilder>().As<IAppEntryLaunchPlanBuilder>().SingleInstance();
        builder.RegisterType<AssociationLaunchResolver>().As<IAssociationLaunchResolver>().SingleInstance();
        builder.RegisterType<AssociationRegistryResolver>().AsSelf().SingleInstance();
        builder.RegisterType<AssociationCommandMaterializer>().AsSelf().SingleInstance();
        builder.RegisterType<LaunchHiveLeaseCoordinator>().AsSelf().SingleInstance();
        builder.RegisterType<ShortcutTargetResolver>().AsSelf().SingleInstance();
        builder.RegisterType<LaunchTargetResolver>().As<ILaunchTargetResolver>().SingleInstance();
        builder.RegisterType<LaunchAccessManager>().As<ILaunchAccessManager>().SingleInstance();
        builder.RegisterType<LaunchFacade>().As<ILaunchFacade>().AsSelf().SingleInstance();
        builder.RegisterType<AppEntryLauncher>().As<IAppEntryLauncher>().AsSelf().SingleInstance();
        builder.RegisterType<CredentialDecryptionService>().As<ICredentialDecryptionService>().SingleInstance();
        builder.RegisterType<LaunchCredentialsLookup>().As<ILaunchCredentialsLookup>().SingleInstance();
        builder.RegisterType<IpcCallerAuthorizer>().As<IIpcCallerAuthorizer>().SingleInstance();
        builder.RegisterType<FolderHandlerService>().As<IFolderHandlerService>().SingleInstance();
        builder.RegisterType<AssociationRegistryWriter>().AsSelf().SingleInstance();
        builder.RegisterType<AssociationAutoSetService>().As<IAssociationAutoSetService>().SingleInstance();

        if (DebugHelper.UseAdminOperationMocks)
        {
            builder.RegisterDecorator<NoOpLsaRightsHelper, ILsaRightsHelper>();
            builder.RegisterDecorator<MockProfileRepairHelper, IProfileRepairHelper>();
            builder.RegisterDecorator<MockAccountProcessLauncher, IAccountProcessLauncher>();
            builder.RegisterDecorator<MockAppContainerProcessLauncher, IAppContainerProcessLauncher>();
            builder.RegisterDecorator<NoOpAssociationAutoSetService, IAssociationAutoSetService>();
        }
    }
}
