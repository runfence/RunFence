using Autofac;
using RunFence.Acl;
using RunFence.Acl.Permissions;
using RunFence.Acl.QuickAccess;
using RunFence.Acl.Traverse;
using RunFence.Acl.UI;
using RunFence.Acl.UI.Forms;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Startup.NonElevatedMocks;

namespace RunFence.Startup.Modules;

public class AclModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        // LocalGroupMembershipService is registered as both its interface and concrete type so
        // callers that need group ops inject ILocalGroupMembershipService directly.
        // Registered here (foundation scope) so AclPermissionService can inject it.
        builder.RegisterType<LocalGroupMembershipService>()
            .As<ILocalGroupMembershipService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<ContainerLookupHelper>().AsSelf().SingleInstance();
        builder.RegisterType<AclAccessor>().As<IAclAccessor>().SingleInstance();
        builder.RegisterType<FileSystemPathInfo>().As<IFileSystemPathInfo>().SingleInstance();
        builder.RegisterType<AclPathIconProvider>().As<IAclPathIconProvider>().SingleInstance();
        builder.RegisterType<TraverseAcl>().As<ITraverseAcl>().SingleInstance();
        builder.RegisterType<GrantTraversePathResolver>().AsSelf().SingleInstance();
        builder.RegisterType<ReparsePointPromptHelper>().As<IReparsePointPromptHelper>().SingleInstance();
        builder.RegisterType<FileSystemAclTraverser>().As<IFileSystemAclTraverser>().SingleInstance();
        builder.RegisterType<LocalSamSidResolver>().As<ILocalSamSidResolver>().SingleInstance();
        builder.RegisterType<CachingLocalUserProvider>().AsSelf().As<ILocalUserProvider>().SingleInstance();
        builder.RegisterType<AclDenyModeService>().As<IAclDenyModeService>().AsSelf().SingleInstance();
        builder.RegisterType<AclAllowModeService>().As<IAclAllowModeService>().AsSelf().SingleInstance();
        builder.RegisterType<AclService>().As<IAclService>().SingleInstance();
        builder.RegisterType<AclPermissionService>().As<IAclPermissionService>().SingleInstance();
        builder.RegisterType<DefaultInteractiveUserResolver>().As<IInteractiveUserResolver>().SingleInstance();
        builder.RegisterType<AncestorTraverseGranter>().AsSelf().SingleInstance();
        builder.RegisterType<LogonScriptTraverseGranter>().AsSelf().SingleInstance();
        builder.RegisterType<DriveAclReplacer>().AsSelf().SingleInstance();
        builder.RegisterType<GrantAceService>()
            .As<IGrantAceService>()
            .As<IGrantInspectionService>()
            .SingleInstance();
        builder.RegisterType<FileOwnerService>().As<IFileOwnerService>().SingleInstance();
        builder.RegisterType<PathExistenceService>().As<IPathExistenceService>().SingleInstance();
        builder.RegisterType<SpecificContainerAceConflictDetector>()
            .As<ISpecificContainerAceConflictDetector>()
            .SingleInstance();
        builder.RegisterType<MandatoryLabelService>().As<IMandatoryLabelService>().SingleInstance();
        builder.RegisterType<GrantCoreOperations>().As<IGrantCoreOperations>().SingleInstance();
        builder.RegisterType<TraverseCoreOperations>().As<ITraverseCoreOperations>().SingleInstance();
        builder.RegisterType<ContainerInteractiveUserSync>().AsSelf().SingleInstance();
        builder.RegisterType<LowIntegrityGrantSync>().AsSelf().SingleInstance();
        builder.RegisterType<PathGrantSyncService>()
            .AsSelf()
            .As<IGrantSyncService>()
            .SingleInstance();
        builder.RegisterType<PathGrantService>()
            .As<IPathGrantService>()
            .As<IGrantMutatorService>()
            .As<ITraverseService>()
            .SingleInstance();
        builder.RegisterType<AclManagerScanService>().As<IAclManagerScanService>().SingleInstance();
        builder.RegisterType<QuickAccessPinService>().As<IQuickAccessPinService>().SingleInstance();

        // AclManagerDialog handlers — InstancePerOwned<AclManagerDialog> so each dialog gets its own
        // set, with shared instances within the same dialog's owned lifetime scope.
        builder.RegisterType<TraverseEntryResolver>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<TraverseAutoManager>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclManagerGrantRowRenderer>().AsSelf().InstancePerDependency();
        builder.RegisterType<AclManagerPendingStateHelper>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclManagerGrantsHelper>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclManagerTraverseOperations>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclManagerTraverseRowBuilder>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclManagerTraverseHelper>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclManagerDragDropHandler>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclManagerActionOrchestrator>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclManagerApplyOrchestrator>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclImportProcessor>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclManagerExportImport>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclManagerPathActionHelper>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclManagerSelectionHandler>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclManagerMouseEventHandler>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclManagerModificationHandler>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclManagerDialog>().AsSelf().InstancePerOwned<AclManagerDialog>();

        // AppEditDialog handlers — InstancePerDependency so each dialog gets its own set
        builder.RegisterType<AclAllowListGridHandler>().AsSelf().InstancePerDependency();
        builder.RegisterType<AclConfigValidator>().AsSelf().InstancePerDependency();
        builder.RegisterType<FolderDepthHelper>().AsSelf().InstancePerDependency();
        builder.RegisterType<AclConfigSection>().AsSelf().InstancePerDependency();

        if (DebugHelper.UseAdminOperationMocks)
        {
            builder.RegisterType<NonElevatedMockStore>().SingleInstance();
            builder.RegisterDecorator<MockLocalGroupMembershipService, ILocalGroupMembershipService>();
            builder.RegisterDecorator<MockLocalUserProvider, ILocalUserProvider>();
        }
    }
}
