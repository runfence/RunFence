using Autofac;
using Autofac.Extras.Ordering;
using RunFence.Account.UI;
using RunFence.Account.UI.AppContainer;
using RunFence.Account.UI.Forms;
using RunFence.Acl;
using RunFence.Acl.UI;
using RunFence.Apps;
using RunFence.Apps.UI;
using RunFence.Apps.UI.Forms;
using RunFence.Core;
using RunFence.DragBridge.UI.Forms;
using RunFence.Groups.UI;
using RunFence.Groups.UI.Forms;
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

namespace RunFence.Startup.Modules;

public class UiModule : Module
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

        builder.Register(c =>
            {
                var lazyForm = c.Resolve<Lazy<MainForm>>();
                return new LambdaUiThreadInvoker(
                    a =>
                    {
                        var form = lazyForm.Value;
                        if (!form.InvokeRequired)
                        {
                            a();
                            return;
                        }
                        form.Invoke(a);
                    },
                    a => lazyForm.Value.BeginInvoke(a));
            })
            .As<IUiThreadInvoker>()
            .SingleInstance();

        builder.RegisterType<SidEntryHelper>()
            .As<ISidEntryHelper>()
            .SingleInstance();

        builder.RegisterType<AllowListEntryFactory>().AsSelf().SingleInstance();

        builder.RegisterType<ExeAssociationRegistryReader>()
            .As<IExeAssociationRegistryReader>()
            .InstancePerDependency();

        builder.RegisterType<AppEditBrowseHelper>()
            .AsSelf()
            .InstancePerDependency();
        builder.RegisterType<AppEditAssociationHandler>().AsSelf().InstancePerDependency();
        builder.RegisterType<AppEditDialogSaveHandler>().AsSelf().InstancePerDependency();
        builder.RegisterType<AppEditAccountSwitchHandler>().AsSelf().InstancePerDependency();
        builder.RegisterType<AppEditDialogInputValidator>().AsSelf().InstancePerDependency();
        builder.RegisterType<AppEditDialogAclConfigBuilder>().AsSelf().InstancePerDependency();
        builder.RegisterType<AppEditDialogController>()
            .AsSelf()
            .InstancePerDependency();
        builder.RegisterType<AppEditDialogSubmitController>()
            .AsSelf()
            .InstancePerDependency();
        builder.RegisterType<AppEditPopulator>().AsSelf().InstancePerDependency();
        builder.RegisterType<AppEditDialogPopulator>().AsSelf().InstancePerDependency();
        builder.RegisterType<AppEditDialogInitializer>().AsSelf().InstancePerDependency();
        builder.RegisterType<AppEditDialogInitializationBinder>().AsSelf().InstancePerDependency();
        builder.RegisterType<AppEditDialog>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<ApplicationsPanelLaunchHandler>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<ApplicationsPanelSaveHelper>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<ApplicationsCrudOperationService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<ApplicationsCrudOrchestrator>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<ApplicationsGridPopulator>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AppGridDragDropHandler>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<DefaultBrowserManager>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AppContextMenuOrchestrator>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<HandlerMappingAddDialog>()
            .As<IHandlerMappingAddDialog>()
            .AsSelf()
            .InstancePerDependency();
        builder.RegisterType<HandlerMappingAddDialogSubmissionState>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<ImportAssociationsDialog>()
            .As<IImportAssociationsDialog>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<HandlerMappingGridBuilder>()
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

        builder.RegisterType<AppContainerEditDialogNotifier>()
            .As<IAppContainerEditDialogNotifier>()
            .SingleInstance();
        builder.RegisterType<AppContainerDialogStateAssembler>()
            .AsSelf()
            .InstancePerDependency();
        builder.RegisterType<AppContainerCapabilitiesBinder>()
            .AsSelf()
            .InstancePerDependency();
        builder.RegisterType<AppContainerDialogResultPresenter>()
            .AsSelf()
            .InstancePerDependency();
        builder.RegisterType<AppContainerEditSubmitController>()
            .AsSelf()
            .InstancePerDependency();
        builder.RegisterType<AppContainerEditDialog>()
            .AsSelf()
            .InstancePerDependency();
        builder.RegisterType<AppContainerProfileActions>()
            .AsSelf()
            .SingleInstance();
        builder.RegisterType<AppContainerEditDialogRunner>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<HandlerMappingMutationHandler>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<HandlerMappingSyncService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<HandlerMappingSubmitTransaction>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<HandlerMappingDialogHelper>()
            .AsSelf()
            .InstancePerDependency();
        builder.RegisterType<HandlerMappingDialogSubmissionCoordinator>()
            .AsSelf()
            .InstancePerDependency();
        builder.RegisterType<HandlerMappingsChildDialogCoordinator>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<HandlerMappingsDialog>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<ApplicationsHandlerSyncHelper>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<EditAccountDialogCreateHandler>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<OperationGuard>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountsPanel>()
            .AsSelf()
            .As<IAccountsPanelContext>()
            .As<IAccountsPanelDataContext>()
            .As<IAccountsPanelOperationContext>()
            .As<IGridSortState>()
            .As<IAccountGridCallbacks>()
            .SingleInstance();
        builder.RegisterType<AccountListPresenter>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<ApplicationsPanel>()
            .As<IWizardRequestSource>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<GroupGridPopulator>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<GroupRefreshController>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<GroupDescriptionEditor>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountAclManagerLauncher>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<GroupSidMigrationLauncher>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<GroupsPanel>()
            .AsSelf()
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

        builder.RegisterType<OptionsPanelCheckboxHandler>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<MainFormFirstRunExporter>()
            .AsSelf()
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

        builder.RegisterType<OptionsIcmpSection>()
            .AsSelf()
            .SingleInstance();

        builder.Register(c =>
            {
                var localUserProvider = c.Resolve<ILocalUserProvider>();
                var sidEntryHelper = c.Resolve<ISidEntryHelper>();
                var displayNameResolver = c.Resolve<SidDisplayNameResolver>();
                return new IpcCallerSection(
                    () => localUserProvider.GetLocalUserAccounts(),
                    sidEntryHelper,
                    displayNameResolver);
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
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AboutPanel>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<OptionsPanel>()
            .As<IDragBridgeSettingsChangeSource>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterInstance(new NotifyIcon())
            .ExternallyOwned();

        builder.RegisterType<AccountCheckTimerService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountContainerOrchestrator>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountMigrationOrchestrator>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<GroupActionOrchestrator>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<GroupBulkScanOrchestrator>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<GroupMembershipOrchestrator>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<MemberPickerDialogAdapter>()
            .As<IMemberPickerDialog>()
            .SingleInstance();

        builder.RegisterType<GroupMembershipPrompt>()
            .As<IGroupMembershipPrompt>()
            .SingleInstance();

        builder.RegisterType<AccountAclBulkScanService>()
            .As<IAccountAclBulkScanService>()
            .SingleInstance();

        builder.RegisterType<AclBulkScanResultProcessor>()
            .As<IAclBulkScanResultProcessor>()
            .SingleInstance();
        builder.RegisterType<AclBulkScanWorkflow>()
            .AsSelf()
            .SingleInstance();
        builder.RegisterType<AccountBulkScanHandler>()
            .As<IAccountBulkScanHandler>()
            .AsSelf()
            .SingleInstance();
        builder.RegisterType<AclBulkScanWarningPresenter>()
            .As<IAclBulkScanWarningPresenter>()
            .SingleInstance();
        builder.RegisterType<AclBulkScanMessagePresenter>()
            .As<IAclBulkScanMessagePresenter>()
            .SingleInstance();
        builder.RegisterType<AclBulkScanResultDialogFactory>()
            .As<IAclBulkScanResultDialogFactory>()
            .SingleInstance();

        builder.RegisterType<StartMenuDiscoveryService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<DiscoveryRefreshManager>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<MainFormStartupOrchestrator>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<MainFormWindowRequestHandler>()
            .As<IElevatedUnlockRequestHandler>()
            .As<IOperationUnlockRequestHandler>()
            .As<IShowWindowRequestHandler>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<MainFormBackgroundAutoLockCoordinator>()
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

        builder.RegisterType<LicenseTitleStartupEventWirer>()
            .As<IStartupEventWirer>()
            .OrderBy(1)
            .SingleInstance();

        builder.RegisterType<LockUiStartupEventWirer>()
            .As<IStartupEventWirer>()
            .OrderBy(2)
            .SingleInstance();

        builder.RegisterType<WizardStartupEventWirer>()
            .As<IStartupEventWirer>()
            .OrderBy(3)
            .SingleInstance();

        builder.RegisterType<SessionSwitchStartupEventWirer>()
            .As<IStartupEventWirer>()
            .OrderBy(4)
            .SingleInstance();

        builder.RegisterType<MessageBoxReencryptionWarningPresenter>()
            .As<IReencryptionWarningPresenter>()
            .SingleInstance();

        builder.RegisterType<SystemSessionSwitchEventSource>()
            .As<ISessionSwitchEventSource>()
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
