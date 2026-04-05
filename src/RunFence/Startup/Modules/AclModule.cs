using Autofac;
using RunFence.Account;
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

        builder.RegisterType<FileSystemAclTraverser>().As<IFileSystemAclTraverser>().SingleInstance();
        builder.RegisterType<CachingLocalUserProvider>().AsSelf().As<ILocalUserProvider>().SingleInstance();
        builder.RegisterType<AclDenyModeService>().AsSelf().SingleInstance();
        builder.RegisterType<AclAllowModeService>().AsSelf().SingleInstance();
        builder.RegisterType<AclService>().As<IAclService>().SingleInstance();
        builder.RegisterType<AclPermissionService>().As<IAclPermissionService>().SingleInstance();
        builder.RegisterType<UserTraverseService>().As<IUserTraverseService>().SingleInstance();
        builder.RegisterType<DefaultInteractiveUserResolver>().As<IInteractiveUserResolver>().SingleInstance();
        builder.RegisterType<PermissionGrantService>().As<IPermissionGrantService>().SingleInstance();
        builder.RegisterType<AncestorTraverseGranter>().AsSelf().SingleInstance();
        builder.RegisterType<GrantedPathAclService>().As<IGrantedPathAclService>().SingleInstance();
        builder.RegisterType<LogonScriptIniManager>().AsSelf().SingleInstance();
        builder.RegisterType<LogonScriptTraverseGranter>().AsSelf().SingleInstance();
        builder.RegisterType<GroupPolicyScriptHelper>().AsSelf().SingleInstance();
        builder.RegisterType<DriveAclReplacer>().AsSelf().SingleInstance();
        builder.RegisterType<AclManagerScanService>().As<IAclManagerScanService>().SingleInstance();
        builder.RegisterType<QuickAccessPinService>().As<IQuickAccessPinService>().SingleInstance();

        // AclManagerDialog handlers — InstancePerOwned<AclManagerDialog> so each dialog gets its own
        // set, with shared instances within the same dialog's owned lifetime scope.
        builder.RegisterType<TraverseAutoManager>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclManagerGrantsHelper>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclManagerTraverseHelper>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclManagerDragDropHandler>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclManagerActionOrchestrator>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<GrantEntryNtfsOperations>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclManagerNtfsApplier>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclManagerDbCommitter>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclManagerInteractiveUserSync>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclManagerApplyOrchestrator>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclManagerExportImport>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclManagerSelectionHandler>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclManagerModificationHandler>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclManagerDialog>().AsSelf().InstancePerOwned<AclManagerDialog>();

        // AppEditDialog handlers — InstancePerDependency so each dialog gets its own set
        builder.RegisterType<AclConfigSection>().AsSelf().InstancePerDependency();
    }
}