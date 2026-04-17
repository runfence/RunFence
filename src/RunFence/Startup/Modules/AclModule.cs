using Autofac;
using RunFence.Acl;
using RunFence.Acl.Permissions;
using RunFence.Acl.QuickAccess;
using RunFence.Acl.Traverse;
using RunFence.Acl.UI;
using RunFence.Acl.UI.Forms;
using RunFence.Infrastructure;

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

        builder.RegisterType<AclAccessor>().As<IAclAccessor>().SingleInstance();
        builder.RegisterType<AclPathIconProvider>().As<IAclPathIconProvider>().SingleInstance();
        builder.RegisterType<TraverseAcl>().As<ITraverseAcl>().SingleInstance();
        builder.RegisterType<ReparsePointPromptHelper>().As<IReparsePointPromptHelper>().SingleInstance();
        builder.RegisterType<FileSystemAclTraverser>().As<IFileSystemAclTraverser>().SingleInstance();
        builder.RegisterType<CachingLocalUserProvider>().AsSelf().As<ILocalUserProvider>().SingleInstance();
        builder.RegisterType<AclDenyModeService>().As<IAclDenyModeService>().AsSelf().SingleInstance();
        builder.RegisterType<AclAllowModeService>().As<IAclAllowModeService>().AsSelf().SingleInstance();
        builder.RegisterType<AclService>().As<IAclService>().SingleInstance();
        builder.RegisterType<AclPermissionService>().As<IAclPermissionService>().SingleInstance();
        builder.RegisterType<DefaultInteractiveUserResolver>().As<IInteractiveUserResolver>().SingleInstance();
        builder.RegisterType<AncestorTraverseGranter>().AsSelf().SingleInstance();
        builder.RegisterType<LogonScriptTraverseGranter>().AsSelf().SingleInstance();
        builder.RegisterType<DriveAclReplacer>().AsSelf().SingleInstance();
        builder.RegisterType<GrantNtfsHelper>().As<IGrantNtfsHelper>().SingleInstance();
        builder.RegisterType<GrantCoreOperations>().AsSelf().SingleInstance();
        builder.RegisterType<TraverseCoreOperations>().AsSelf().SingleInstance();
        builder.RegisterType<ContainerInteractiveUserSync>().AsSelf().SingleInstance();
        builder.RegisterType<PathGrantSyncService>().AsSelf().SingleInstance();
        builder.RegisterType<PathGrantService>()
            .As<IPathGrantService>()
            .As<IGrantMutatorService>()
            .As<ITraverseService>()
            .As<IGrantInspectionService>()
            .As<IGrantSyncService>()
            .SingleInstance();
        builder.RegisterType<AclManagerScanService>().As<IAclManagerScanService>().SingleInstance();
        builder.RegisterType<QuickAccessPinService>().As<IQuickAccessPinService>().SingleInstance();

        // AclManagerDialog handlers — InstancePerOwned<AclManagerDialog> so each dialog gets its own
        // set, with shared instances within the same dialog's owned lifetime scope.
        builder.RegisterType<TraverseEntryResolver>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<TraverseAutoManager>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclManagerGrantRowRenderer>().AsSelf().InstancePerDependency();
        builder.RegisterType<AclManagerGrantsHelper>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclManagerTraverseHelper>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclManagerDragDropHandler>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclManagerActionOrchestrator>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclManagerApplyOrchestrator>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclManagerExportImport>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclManagerSelectionHandler>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclManagerModificationHandler>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclManagerDialog>().AsSelf().InstancePerOwned<AclManagerDialog>();

        // AppEditDialog handlers — InstancePerDependency so each dialog gets its own set
        builder.RegisterType<AclAllowListGridHandler>().AsSelf().InstancePerDependency();
        builder.RegisterType<AclConfigValidator>().AsSelf().InstancePerDependency();
        builder.RegisterType<AclConfigSection>().AsSelf().InstancePerDependency();
    }
}