using Autofac;
using RunFence.RunAs;
using RunFence.RunAs.UI;
using RunFence.RunAs.UI.Forms;

namespace RunFence.Startup.Modules;

public class RunAsModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<RunAsFlowHandler>()
            .As<IRunAsFlowHandler>()
            .SingleInstance();

        builder.RegisterType<RunAsDialogPresenter>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<RunAsPostDialogRouter>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<RunAsPermissionApplier>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<RunAsLaunchDispatcher>()
            .As<IRunAsLaunchDispatcher>()
            .SingleInstance();

        builder.RegisterType<RunAsResultProcessor>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<RunAsAccountCreationUI>()
            .As<IRunAsAccountCreationUI>()
            .As<IRunAsContainerCreationUI>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<RunAsCredentialCreator>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<RunAsUserAccountCreator>()
            .As<IRunAsUserAccountCreator>()
            .SingleInstance();

        builder.RegisterType<RunAsContainerCreator>()
            .As<IRunAsContainerCreator>()
            .SingleInstance();

        builder.RegisterType<RunAsLaunchErrorHandler>()
            .As<IRunAsLaunchErrorHandler>()
            .SingleInstance();

        builder.RegisterType<RunAsAppShortcutCreator>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AppEntryPersistenceOrchestrator>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<RunAsAppEntryManager>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AppEditCommitService>()
            .As<IAppEditCommitService>()
            .SingleInstance();

        builder.RegisterType<RunAsAppEditDialogHandler>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<RunAsDirectLauncher>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<RunAsPermissionChecker>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<RunAsPermissionPromptHelper>()
            .As<IRunAsPermissionPromptHelper>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<RunAsCredentialPersister>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<RunAsCreatedAccountPersistenceCoordinator>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<RunAsCreatedAccountPostSetupService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<RunAsAccountCreationErrorPresenter>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<RunAsAccountSettingsApplier>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AppEntryPermissionPrompter>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<RunAsDosProtection>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<RunAsCredentialListPopulator>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<RunAsCredentialListRenderer>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<RunAsAccountOptionCatalog>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<RunAsSelectionPolicy>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<RunAsPasswordDialogAdapterFactory>()
            .As<IRunAsPasswordDialogAdapterFactory>()
            .InstancePerDependency();

        builder.RegisterType<RunAsAdHocPasswordPromptService>()
            .As<IRunAsAdHocPasswordPromptService>()
            .InstancePerDependency();

        builder.RegisterType<RunAsAncestorPermissionPrompter>()
            .As<IRunAsAncestorPermissionPrompter>()
            .InstancePerDependency();

        builder.RegisterType<RunAsDialog>()
            .AsSelf()
            .InstancePerDependency();
    }
}
