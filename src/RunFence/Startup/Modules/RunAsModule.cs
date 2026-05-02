using Autofac;
using Autofac.Core.Activators.Reflection;
using BindingFlags = System.Reflection.BindingFlags;
using RunFence.RunAs;
using RunFence.RunAs.UI;
using RunFence.RunAs.UI.Forms;

namespace RunFence.Startup.Modules;

public class RunAsModule : Module
{
    private static readonly IConstructorFinder AllInstanceConstructors =
        new DefaultConstructorFinder(type => type.GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));

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
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<RunAsCredentialPersister>()
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

        builder.RegisterType<RunAsDialog>()
            .FindConstructorsWith(AllInstanceConstructors)
            .AsSelf()
            .InstancePerDependency();
    }
}
