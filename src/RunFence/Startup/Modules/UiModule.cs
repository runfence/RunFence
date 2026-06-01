using Autofac;
using Autofac.Extras.Ordering;
using RunFence.Infrastructure;
using RunFence.Startup.UI;
namespace RunFence.Startup.Modules;

public class UiModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterModule<AppsUiModule>();
        builder.RegisterModule<AccountsUiModule>();
        builder.RegisterModule<AclUiModule>();
        builder.RegisterModule<WizardUiModule>();
        builder.RegisterModule<SharedUiModule>();

        builder.RegisterType<StartupEnforcementRunner>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<StartupFeatureActivator>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<DeferredStartupRunner>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AppLifecycleStarter>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<DragBridgeEventWirer>()
            .As<IStartupEventWirer>()
            .OrderBy(5)
            .SingleInstance();

        builder.RegisterType<DataRefreshStartupEventWirer>()
            .As<IStartupEventWirer>()
            .OrderBy(0)
            .SingleInstance();

        builder.RegisterType<LockUiStartupEventWirer>()
            .As<IStartupEventWirer>()
            .OrderBy(1)
            .SingleInstance();

        builder.RegisterType<WizardStartupEventWirer>()
            .As<IStartupEventWirer>()
            .OrderBy(2)
            .SingleInstance();

        builder.RegisterType<SessionSwitchStartupEventWirer>()
            .As<IStartupEventWirer>()
            .OrderBy(3)
            .SingleInstance();

        builder.RegisterType<StartupIpcBootstrapper>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountConfigTransferOrchestrator>()
            .AsSelf()
            .SingleInstance();
        builder.RegisterType<AccountConfigTransferSecureDesktopService>()
            .As<IAccountConfigTransferSecureDesktopService>()
            .SingleInstance();
        builder.RegisterType<AccountConfigTransferPromptService>()
            .As<IAccountConfigTransferPromptService>()
            .SingleInstance();

        builder.RegisterType<UserConfirmationService>()
            .As<IUserConfirmationService>()
            .SingleInstance();
    }
}
