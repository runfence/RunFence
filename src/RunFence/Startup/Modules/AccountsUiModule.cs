using Autofac;
using RunFence.Account.UI;
using RunFence.Account.UI.AppContainer;
using RunFence.Account.UI.Forms;
using RunFence.Core;
using RunFence.Groups;
using RunFence.Groups.UI;
using RunFence.Groups.UI.Forms;
using RunFence.Infrastructure;

namespace RunFence.Startup.Modules;

public class AccountsUiModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
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

        builder.RegisterType<EditAccountDialogCreateHandler>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountGridRowComposer>()
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

        builder.RegisterType<GroupGridPopulator>()
            .AsSelf()
            .As<IGroupGridPopulator>()
            .SingleInstance();

        builder.RegisterType<GroupRefreshController>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<GroupDescriptionEditor>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<GroupSelectionLoadController>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<GroupSidMigrationLauncher>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<GroupsPanel>()
            .AsSelf()
            .SingleInstance();

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

        builder.RegisterType<GroupDeletePrompt>()
            .As<IGroupDeletePrompt>()
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
    }
}
