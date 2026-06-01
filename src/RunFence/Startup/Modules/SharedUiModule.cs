using Autofac;
using Autofac.Extras.Ordering;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.DragBridge.UI.Forms;
using RunFence.Groups;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Licensing.UI.Forms;
using RunFence.Persistence.UI.Forms;
using RunFence.Startup;
using RunFence.Startup.UI;
using RunFence.TrayIcon;
using RunFence.UI;
using RunFence.UI.Forms;
using System.Windows.Forms;

namespace RunFence.Startup.Modules;

public class SharedUiModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<MainForm>()
            .As<ITrayOwner>()
            .As<IMainFormVisibility>()
            .As<IMainFormDataRefreshTarget>()
            .As<IMainFormLockTarget>()
            .As<IStartupFormLifetime>()
            .As<IStartupIpcHost>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<MainUiThreadContext>()
            .AsSelf()
            .As<IUiThreadInvoker>()
            .SingleInstance();

        builder.RegisterType<SidEntryHelper>()
            .As<ISidEntryHelper>()
            .SingleInstance();

        builder.RegisterType<WinFormsUiIconService>()
            .As<IUiIconService>()
            .SingleInstance();

        builder.RegisterType<FullModeAccountLaunchIdentityFactory>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<MessageBoxService>()
            .As<IMessageBoxService>()
            .SingleInstance();

        builder.RegisterType<AccountMessageBoxService>()
            .As<IAccountMessageBoxService>()
            .SingleInstance();

        builder.RegisterType<ShellHelper>()
            .As<IShellHelper>()
            .SingleInstance();

        builder.RegisterType<ClipboardTextService>()
            .As<IClipboardTextService>()
            .SingleInstance();

        builder.RegisterType<OptionsSettingsHandler>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<StartWithoutPinPromptService>()
            .As<IStartWithoutPinPromptService>()
            .SingleInstance();

        builder.RegisterType<StartWithoutPinRotationRunner>()
            .As<IStartWithoutPinRotationRunner>()
            .SingleInstance();

        builder.RegisterType<OptionsStartWithoutPinHandler>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<OptionsMaintenanceLaunchHandler>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<OptionsFolderBrowserHandler>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<OptionsDesktopSettingsHandler>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<OptionsFolderBrowserSection>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<OptionsForegroundPrivilegeMarkerSection>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<OptionsPanelCheckboxHandler>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<MainFormFirstRunExporter>()
            .As<IMainFormFirstRunExporter>()
            .SingleInstance();

        builder.RegisterType<FindingLocationHelper>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<SecurityCheckRunner>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<OptionsPanelDataLoader>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<ApplicationCaptionTextBuilder>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<ForegroundMarkerTrayStatusController>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<OptionsIcmpSection>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<IpcCallerModalService>()
            .As<IIpcCallerModalService>()
            .SingleInstance();

        builder.Register(c =>
            {
                var accountQueryService = c.Resolve<IWindowsAccountQueryService>();
                var sidEntryHelper = c.Resolve<ISidEntryHelper>();
                var displayNameResolver = c.Resolve<SidDisplayNameResolver>();
                var modalService = c.Resolve<IIpcCallerModalService>();
                return new IpcCallerSection(
                    accountQueryService,
                    sidEntryHelper,
                    displayNameResolver,
                    modalService);
            })
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<ConfigManagerSection>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<DragBridgeSection>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<TrayMenuDiscoveryBuilder>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<TrayMenuBuilder>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<TrayIconManager>()
            .As<IInputInjectionTraySink>()
            .As<ITrayForegroundMarkerOverlaySink>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<TrayIconOverlayRenderer>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AboutPanel>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<OptionsPanelLifecycleCoordinator>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<OptionsPanel>()
            .As<IDragBridgeSettingsChangeSource>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterInstance(new NotifyIcon())
            .ExternallyOwned();

        builder.RegisterType<StartMenuDiscoveryService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<DiscoveryRefreshManager>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<MainFormStartupOrchestrator>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<MainFormContentCoordinator>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<MainFormMessageRouter>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<MainFormWindowRequestHandler>()
            .As<IElevatedUnlockRequestHandler>()
            .As<IOperationUnlockRequestHandler>()
            .As<IShowWindowRequestHandler>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<MainFormBackgroundAutoLockCoordinator>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<WinFormsApplicationExitService>()
            .As<IApplicationExitService>()
            .SingleInstance();

        builder.RegisterType<TrayIdleMonitorCoordinator>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<MainFormTrayHandler>()
            .As<ITrayBalloonService>()
            .As<ITrayMenuActionHandler>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<TrayWarningSink>()
            .As<ITrayWarningSink>()
            .SingleInstance();

        builder.RegisterType<LaunchFeedbackPresenter>()
            .As<ILaunchFeedbackPresenter>()
            .SingleInstance();

        builder.RegisterType<WinFormsUiTimerFactory>()
            .As<IUiTimerFactory>()
            .SingleInstance();

        builder.RegisterType<TrayLaunchHandler>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<EnforcementResultApplier>().AsSelf().SingleInstance();

        builder.RegisterType<MessageBoxReencryptionWarningPresenter>()
            .As<IReencryptionWarningPresenter>()
            .SingleInstance();

        builder.RegisterType<MessageBoxStartupEnforcementMessagePresenter>()
            .As<IStartupEnforcementMessagePresenter>()
            .SingleInstance();

        builder.RegisterType<MessageBoxStartupRepairWarningPresenter>()
            .As<IStartupRepairWarningPresenter>()
            .SingleInstance();

        builder.RegisterType<SystemSessionSwitchEventSource>()
            .As<ISessionSwitchEventSource>()
            .SingleInstance();

    }
}
