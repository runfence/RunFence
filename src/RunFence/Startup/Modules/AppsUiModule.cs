using Autofac;
using RunFence.Apps;
using RunFence.Apps.UI;
using RunFence.Apps.UI.Forms;
using RunFence.Infrastructure;

namespace RunFence.Startup.Modules;

public class AppsUiModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<ExeAssociationRegistryReader>()
            .As<IExeAssociationRegistryReader>()
            .InstancePerDependency();

        builder.RegisterType<AppEditBrowseHelper>()
            .AsSelf()
            .InstancePerDependency();
        builder.RegisterType<AppEntryHandlerPathSuggestionService>()
            .AsSelf()
            .InstancePerDependency();
        builder.RegisterType<HandlerCommandTargetRegistryReader>()
            .As<IHandlerCommandTargetReader>()
            .InstancePerDependency();
        builder.RegisterType<AppEntryEditPathRepairSuggester>()
            .AsSelf()
            .InstancePerDependency();
        builder.RegisterType<HandlerPathIconProbe>()
            .As<IHandlerPathIconProbe>()
            .InstancePerDependency();
        builder.RegisterType<AppDiscoveryDialogService>()
            .As<IAppDiscoveryDialogService>()
            .SingleInstance();
        builder.RegisterType<HandlerAssociationMutationService>()
            .As<IHandlerAssociationMutationService>()
            .InstancePerDependency();
        builder.RegisterType<HandlerAssociationsChildDialogCoordinator>()
            .AsSelf()
            .InstancePerDependency();
        builder.RegisterType<HandlerAssociationEditDialog>()
            .AsSelf()
            .InstancePerDependency();
        builder.RegisterType<AppEditAssociationHandler>().AsSelf().InstancePerDependency();
        builder.RegisterType<AppEditDialogSaveHandler>().AsSelf().InstancePerDependency();
        builder.RegisterType<AppEntryChangeClassifier>().AsSelf().SingleInstance();
        builder.RegisterType<AppEditAccountSwitchHandler>().AsSelf().InstancePerDependency();
        builder.RegisterType<AppEditDialogInputValidator>().AsSelf().InstancePerDependency();
        builder.RegisterType<AppEditDialogAclConfigBuilder>().AsSelf().InstancePerDependency();
        builder.RegisterType<AppEditDialogController>()
            .AsSelf()
            .InstancePerDependency();
        builder.RegisterType<AppEditDialogSnapshotProvider>()
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
        builder.RegisterType<ApplicationsPanelCommandCoordinator>()
            .AsSelf()
            .InstancePerDependency();
        builder.RegisterType<ApplicationsPanelRefreshCoordinator>()
            .AsSelf()
            .InstancePerDependency();

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

        builder.RegisterType<ApplicationsPanel>()
            .As<IWizardRequestSource>()
            .AsSelf()
            .SingleInstance();
    }
}
