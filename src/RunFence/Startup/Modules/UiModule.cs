using Autofac;
using RunFence.Account.UI;
using RunFence.Account.UI.Forms;
using RunFence.Acl;
using RunFence.Acl.UI;
using RunFence.Apps;
using RunFence.Apps.UI;
using RunFence.Apps.UI.Forms;
using RunFence.DragBridge.UI.Forms;
using RunFence.Groups.UI;
using RunFence.Groups.UI.Forms;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Licensing.UI.Forms;
using RunFence.Persistence.UI.Forms;
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
            .AsSelf()
            .SingleInstance();

        builder.Register(c =>
            {
                var lazyForm = c.Resolve<Lazy<MainForm>>();
                var log = c.Resolve<ILoggingService>();
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
                    a => lazyForm.Value.BeginInvoke(a),
                    a =>
                    {
                        var form = lazyForm.Value;
                        if (!form.InvokeRequired)
                        {
                            a();
                            return;
                        }
                        // Probe whether the UI thread is pumping: post a no-op and wait up to
                        // 1 s. If it completes, the thread is responsive and Invoke is safe.
                        // If it times out, the UI thread is blocked — fall back to BeginInvoke.
                        using var probe = new ManualResetEventSlim(false);
                        form.BeginInvoke(() => { try { probe.Set(); } catch (ObjectDisposedException) { } });
                        if (probe.Wait(1000))
                            form.Invoke(a);
                        else
                        {
                            log.Warn($"RunOnUiThread: UI thread blocked, falling back to BeginInvoke.\n{new System.Diagnostics.StackTrace()}");
                            form.BeginInvoke(a);
                        }
                    });
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

        builder.RegisterType<AppEditBrowseHelper>().AsSelf().InstancePerDependency();
        builder.RegisterType<AppEditAssociationHandler>().AsSelf().InstancePerDependency();
        builder.RegisterType<AppEditAccountSwitchHandler>().AsSelf().InstancePerDependency();
        builder.RegisterType<AppEditDialogController>().AsSelf().InstancePerDependency();
        builder.RegisterType<AppEditPopulator>().AsSelf().InstancePerDependency();
        builder.RegisterType<AppEditDialogPopulator>().AsSelf().InstancePerDependency();
        builder.RegisterType<AppEditDialog>()
            .AsSelf()
            .InstancePerDependency();

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
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<HandlerMappingEditDirectDialog>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<HandlerMappingEditAppDialog>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<HandlerMappingGridBuilder>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<HandlerMappingMutationHandler>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<HandlerMappingSyncService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<DirectHandlerResolver>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<HandlerMappingsController>()
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
            .As<IGridSortState>()
            .As<IAccountGridCallbacks>()
            .SingleInstance();

        builder.RegisterType<ApplicationsPanel>()
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

        builder.RegisterType<OptionsAutoFeatureHandler>()
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

        builder.RegisterType<TrayIconManager>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AboutPanel>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<OptionsPanel>()
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

        builder.RegisterType<AccountBulkScanHandler>()
            .As<IAccountBulkScanHandler>()
            .AsSelf()
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

        builder.RegisterType<MainFormTrayHandler>()
            .AsSelf()
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

        builder.RegisterType<AppLifecycleEventWirer>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<StartupIpcBootstrapper>()
            .AsSelf()
            .SingleInstance();
    }
}