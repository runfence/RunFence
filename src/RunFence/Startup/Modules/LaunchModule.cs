using Autofac;
using Microsoft.Win32;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Helpers;
using RunFence.Infrastructure;
using RunFence.Ipc;
using RunFence.Launch;
using RunFence.Launch.Container;
using RunFence.Launch.Tokens;
using RunFence.Persistence;
using RunFence.Startup;
using RunFence.Startup.NonElevatedMocks;

namespace RunFence.Startup.Modules;

public class LaunchModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<MessageBoxProfileRepairPrompt>()
            .As<IProfileRepairPrompt>()
            .SingleInstance();
        builder.RegisterType<RunFenceRestartService>()
            .As<IRunFenceRestartService>()
            .SingleInstance();
        builder.RegisterType<ProfileRepairHelper>()
            .As<IProfileRepairHelper>()
            .SingleInstance();
        builder.RegisterType<LsaRightsHelper>().As<ILsaRightsHelper>().SingleInstance();
        builder.RegisterType<InteractiveLogonHelper>().As<IInteractiveLogonHelper>().SingleInstance();
        builder.RegisterType<ExplorerTokenProvider>().As<IExplorerTokenProvider>().SingleInstance();
        builder.RegisterType<SystemTokenProvider>().As<ISystemTokenProvider>().SingleInstance();
        builder.RegisterType<SystemPrivilegeRunner>().As<ISystemPrivilegeRunner>().SingleInstance();
        builder.RegisterType<LogonTokenProvider>().As<ILogonTokenProvider>().SingleInstance();
        builder.RegisterType<AppContainerComRegistryRoots>().As<IAppContainerComRegistryRoots>().SingleInstance();
        builder.RegisterType<AppContainerUserRegistryRoot>().As<IAppContainerUserRegistryRoot>().SingleInstance();
        builder.RegisterType<AppContainerPathProvider>().As<IAppContainerPathProvider>().SingleInstance();
        builder.RegisterType<AppContainerEnvironmentSetup>().As<IAppContainerEnvironmentSetup>().SingleInstance();
        builder.RegisterType<AppContainerProfileSetup>().As<IAppContainerProfileSetup>().SingleInstance();
        builder.RegisterType<AppContainerTokenNativeApi>().As<IAppContainerTokenNativeApi>().SingleInstance();
        builder.RegisterType<AppContainerTokenBuilder>().As<IAppContainerTokenBuilder>().SingleInstance();
        builder.RegisterType<AppContainerProcessStarter>().As<IAppContainerProcessStarter>().SingleInstance();
        builder.RegisterType<AppContainerDataFolderService>().As<IAppContainerDataFolderService>().SingleInstance();
        builder.RegisterType<AppContainerComAccessService>().As<IAppContainerComAccessService>().InstancePerDependency();
        builder.RegisterType<AppContainerSidProvider>().As<IAppContainerSidProvider>().SingleInstance();
        builder.RegisterType<AppContainerProcessLauncher>().As<IAppContainerProcessLauncher>().SingleInstance();
        builder.RegisterType<ElevatedLinkedTokenProvider>().As<IElevatedLinkedTokenProvider>().SingleInstance();
        builder.RegisterType<SaferDeElevationHelper>().As<ISaferDeElevationHelper>().SingleInstance();
        builder.RegisterType<TokenIntegrityLevelService>().As<ITokenIntegrityLevelService>().SingleInstance();
        builder.RegisterType<DefaultDesktopProfileKeeperBootstrapContext>().As<IProfileKeeperBootstrapContext>().SingleInstance();
        builder.RegisterType<RestrictedProcessControl>().As<IRestrictedProcessControl>().SingleInstance();
        builder.RegisterType<RestrictedProcessActivationGuard>().AsSelf().SingleInstance();
        builder.RegisterType<JobKeeperLaunchProcessApi>().As<IJobKeeperLaunchProcessApi>().SingleInstance();
        builder.RegisterType<PreparedTokenProcessLauncher>().As<IPreparedTokenProcessLauncher>().SingleInstance();
        builder.RegisterType<RestrictedJobLaunchCoordinator>()
            .WithParameter(
                "jobKeeperExePath",
                Path.Combine(AppContext.BaseDirectory, PathConstants.JobKeeperExeName))
            .As<IRestrictedJobLaunchCoordinator>()
            .AsSelf()
            .SingleInstance();
        builder.RegisterType<AccountProcessLauncher>().As<IAccountProcessLauncher>().SingleInstance();
        builder.RegisterType<CreateProcessLauncherHelper>()
            .WithParameter(
                "profileKeeperExePath",
                Path.Combine(AppContext.BaseDirectory, PathConstants.ProfileKeeperExeName))
            .As<ICreateProcessLauncherHelper>()
            .SingleInstance();
        builder.RegisterType<AppContainerService>().As<IAppContainerService>().As<IAppContainerProfileService>().SingleInstance();
        builder.RegisterType<ProcessLauncher>().As<IProcessLauncher>().SingleInstance();
        builder.RegisterType<WindowsAppsPackageRegistrationRepairer>().As<IWindowsAppsPackageRegistrationRepairer>().SingleInstance();
        builder.RegisterType<WindowsAppsRepairProcessLauncher>().As<IWindowsAppsRepairProcessLauncher>().SingleInstance();
        builder.RegisterType<WindowsAppsRegistrationRepairRunner>().As<IWindowsAppsRegistrationRepairRunner>().SingleInstance();
        builder.RegisterType<LaunchDefaultsResolver>().As<ILaunchDefaultsResolver>().SingleInstance();
        builder.RegisterType<AppEntryLaunchPlanBuilder>().As<IAppEntryLaunchPlanBuilder>().SingleInstance();
        builder.RegisterType<AssociationLaunchResolver>().As<IAssociationLaunchResolver>().SingleInstance();
        builder.RegisterType<WindowsAppsPackagePathRepairer>().As<IWindowsAppsPackagePathRepairer>().SingleInstance();
        builder.RegisterType<AssociationExecutablePathResolver>().As<IAssociationExecutablePathResolver>().SingleInstance();
        builder.RegisterType<AssociationRegistryResolver>().AsSelf().SingleInstance();
        builder.RegisterType<AssociationCommandMaterializer>().AsSelf().SingleInstance();
        builder.RegisterType<LaunchHiveLeaseCoordinator>().AsSelf().SingleInstance();
        builder.RegisterType<ShortcutTargetResolver>().AsSelf().SingleInstance();
        builder.RegisterType<LaunchTargetResolver>().As<ILaunchTargetResolver>().SingleInstance();
        builder.Register(ctx =>
            {
                var sessionProvider = ctx.Resolve<SessionProvider>();
                return new Func<IHandlerMappingService>(() => sessionProvider.GetSessionScope().Resolve<IHandlerMappingService>());
            })
            .SingleInstance();
        builder.RegisterType<LaunchAccessManager>().As<ILaunchAccessManager>().SingleInstance();
        builder.RegisterType<LaunchFacade>().As<ILaunchFacade>().AsSelf().SingleInstance();
        builder.RegisterType<AppEntryLauncher>().As<IAppEntryLauncher>().AsSelf().SingleInstance();
        builder.RegisterType<CredentialDecryptionService>().As<ICredentialDecryptionService>().SingleInstance();
        builder.RegisterType<LaunchCredentialsLookup>().As<ILaunchCredentialsLookup>().SingleInstance();
        builder.RegisterType<IpcCallerAuthorizer>().As<IIpcCallerAuthorizer>().SingleInstance();
        builder.RegisterType<FolderHandlerSidLockProvider>().SingleInstance();
        builder.RegisterType<FolderHandlerRegistryStore>().SingleInstance();
        builder.RegisterType<FolderHandlerRegistrationRollback>().SingleInstance();
        builder.RegisterType<FolderHandlerService>().As<IFolderHandlerService>().SingleInstance();
        builder.RegisterType<AssociationRegistryWriter>().AsSelf().SingleInstance();
        builder.RegisterType<AssociationFallbackRegistry>()
            .AsSelf()
            .InstancePerDependency();
        builder.RegisterType<AssociationFallbackRegistry>()
            .WithParameter("usersRoot", Registry.Users)
            .As<IAssociationFallbackRegistry>()
            .InstancePerDependency();
        builder.RegisterType<AssociationFallbackRestoreService>().AsSelf().InstancePerDependency();
        builder.Register(context =>
            {
                var scope = context.Resolve<ILifetimeScope>();
                return new Func<RegistryKey, AssociationFallbackRegistry>(
                    usersRoot => scope.Resolve<AssociationFallbackRegistry>(
                        new NamedParameter("usersRoot", usersRoot)));
            })
            .As<Func<RegistryKey, AssociationFallbackRegistry>>();
        builder.Register(context =>
            {
                var scope = context.Resolve<ILifetimeScope>();
                return new Func<IAssociationFallbackRegistry, AssociationFallbackRestoreService>(
                    registry => scope.Resolve<AssociationFallbackRestoreService>(
                        new TypedParameter(typeof(IAssociationFallbackRegistry), registry)));
            })
            .As<Func<IAssociationFallbackRegistry, AssociationFallbackRestoreService>>();
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
