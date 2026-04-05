using Autofac;
using RunFence.Account;
using RunFence.Account.UI;
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
        builder.RegisterType<AppContainerEnvironmentSetup>().As<IAppContainerEnvironmentSetup>().SingleInstance();
        builder.RegisterType<AppContainerProfileSetup>().AsSelf().SingleInstance();
        builder.RegisterType<SplitTokenLauncher>().As<ISplitTokenLauncher>().SingleInstance();
        builder.RegisterType<LowIntegrityLauncher>().As<ILowIntegrityLauncher>().SingleInstance();
        builder.RegisterType<InteractiveUserLauncher>().As<IInteractiveUserLauncher>().SingleInstance();
        builder.RegisterType<CurrentAccountLauncher>().As<ICurrentAccountLauncher>().SingleInstance();
        builder.RegisterType<ProcessLaunchService>().As<IProcessLaunchService>().SingleInstance();
        builder.RegisterType<AppContainerService>().As<IAppContainerService>().As<IAppContainerProfileService>().SingleInstance();
        builder.RegisterType<AccountLauncher>().As<IAccountLauncher>().AsSelf().SingleInstance();
        builder.RegisterType<AppLaunchOrchestrator>().As<IAppLaunchOrchestrator>().SingleInstance();
        builder.RegisterDecorator<ProfileRepairLaunchDecorator, IAppLaunchOrchestrator>();
        builder.RegisterType<IpcCallerAuthorizer>().As<IIpcCallerAuthorizer>().SingleInstance();
        builder.RegisterType<FolderHandlerService>().As<IFolderHandlerService>().SingleInstance();
    }
}