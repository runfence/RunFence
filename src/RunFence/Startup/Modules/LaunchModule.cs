using Autofac;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Apps;
using RunFence.Ipc;
using RunFence.Launch;
using RunFence.Launch.Container;
using RunFence.Launch.Tokens;

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
        builder.RegisterType<LogonTokenProvider>().AsSelf().SingleInstance();
        builder.RegisterType<AppContainerEnvironmentSetup>().As<IAppContainerEnvironmentSetup>().SingleInstance();
        builder.RegisterType<AppContainerProfileSetup>().AsSelf().SingleInstance();
        builder.RegisterType<AppContainerDataFolderService>().AsSelf().SingleInstance();
        builder.RegisterType<AppContainerComAccessService>().AsSelf().InstancePerDependency();
        builder.RegisterType<AppContainerSidProvider>().As<IAppContainerSidProvider>().SingleInstance();
        builder.RegisterType<AppContainerProcessLauncher>().As<IAppContainerProcessLauncher>().SingleInstance();
        builder.RegisterType<ElevatedLinkedTokenProvider>().AsSelf().SingleInstance();
        builder.RegisterType<SaferDeElevationHelper>().AsSelf().SingleInstance();
        builder.RegisterType<AccountProcessLauncher>().As<IAccountProcessLauncher>().SingleInstance();
        builder.RegisterType<CreateProcessLauncherHelper>().AsSelf().SingleInstance();
        builder.RegisterType<AppContainerService>().As<IAppContainerService>().As<IAppContainerProfileService>().SingleInstance();
        builder.RegisterType<ProcessLauncher>().As<IProcessLauncher>().SingleInstance();
        builder.RegisterType<LaunchDefaultsResolver>().As<ILaunchDefaultsResolver>().SingleInstance();
        builder.RegisterType<LaunchFacade>().As<ILaunchFacade>().AsSelf().SingleInstance();
        builder.RegisterType<AppEntryLauncher>().As<IAppEntryLauncher>().AsSelf().SingleInstance();
        builder.RegisterType<CredentialDecryptionService>().As<ICredentialDecryptionService>().SingleInstance();
        builder.RegisterType<LaunchCredentialsLookup>().As<ILaunchCredentialsLookup>().SingleInstance();
        builder.RegisterType<IpcCallerAuthorizer>().As<IIpcCallerAuthorizer>().SingleInstance();
        builder.RegisterType<FolderHandlerService>().As<IFolderHandlerService>().SingleInstance();
        builder.RegisterType<AssociationAutoSetService>().As<IAssociationAutoSetService>().SingleInstance();
    }
}