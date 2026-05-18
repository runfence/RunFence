using Autofac;
using RunFence.Acl;
using RunFence.Acl.Permissions;
using RunFence.Acl.QuickAccess;
using RunFence.Acl.Traverse;
using RunFence.Acl.UI;
using RunFence.Acl.UI.Forms;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.Startup;
using RunFence.Startup.NonElevatedMocks;

namespace RunFence.Startup.Modules;

public class AclModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<LocalGroupQueryService>()
            .As<ILocalGroupQueryService>()
            .As<ILocalGroupQueryMaintenanceService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<LocalGroupMutationService>()
            .As<ILocalGroupMutationService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<LocalGroupMembershipService>()
            .As<ILocalGroupMembershipService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<ContainerLookupHelper>().AsSelf().SingleInstance();
        builder.RegisterType<BackupPrivilegeSecurityNative>().As<IBackupPrivilegeSecurityNative>().SingleInstance();
        builder.RegisterType<BackupPrivilegeSecurityDescriptorAccessor>().AsSelf().SingleInstance();
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
        builder.RegisterType<DeterministicAclAccessEvaluator>().As<IAclAccessEvaluator>().SingleInstance();
        builder.RegisterType<AclPermissionService>().As<IAclPermissionService>().SingleInstance();
        builder.RegisterType<DefaultInteractiveUserResolver>().As<IInteractiveUserResolver>().SingleInstance();
        builder.RegisterType<AncestorTraverseGranter>().AsSelf().SingleInstance();
        builder.RegisterType<LogonScriptTraverseGranter>()
            .As<ILogonScriptTraverseGranter>()
            .AsSelf()
            .SingleInstance();
        builder.RegisterType<DriveAclReplacer>().AsSelf().SingleInstance();
        builder.RegisterType<GrantAceService>()
            .As<IGrantAceService>()
            .As<IGrantInspectionService>()
            .SingleInstance();
        builder.RegisterType<FileOwnerService>().As<IFileOwnerService>().SingleInstance();
        builder.RegisterType<SpecificContainerAceConflictDetector>()
            .As<ISpecificContainerAceConflictDetector>()
            .SingleInstance();
        builder.RegisterType<MandatoryLabelService>().As<IMandatoryLabelService>().SingleInstance();
        builder.RegisterType<GrantCoreOperations>().As<IGrantCoreOperations>().SingleInstance();
        builder.RegisterType<TraverseGrantOwnerResolver>()
            .As<ITraverseGrantOwnerResolver>()
            .SingleInstance();
        builder.RegisterType<TraverseIntentStoreCoordinator>()
            .As<ITraverseIntentStoreCoordinator>()
            .SingleInstance();
        builder.RegisterType<TraverseGrantStateService>().AsSelf().SingleInstance();
        builder.RegisterType<TraverseCoreOperations>().As<ITraverseCoreOperations>().SingleInstance();
        builder.RegisterType<GrantIntentStoreSaveService>()
            .As<IGrantIntentStoreSaveService>()
            .SingleInstance();
        builder.RegisterType<ContainerInteractiveUserSync>().AsSelf().SingleInstance();
        builder.RegisterType<LowIntegrityGrantSync>().AsSelf().SingleInstance();
        builder.RegisterType<GrantFileSystemOperations>().AsSelf().SingleInstance();
        builder.RegisterType<GrantAccessEnsurer>().AsSelf().SingleInstance();
        builder.RegisterType<TraverseRestoreWorkflow>().AsSelf().SingleInstance();
        builder.Register(ctx =>
            {
                var sessionProvider = ctx.Resolve<SessionProvider>();
                return new Func<IGrantIntentRepository>(() => sessionProvider.GetSessionScope().Resolve<IGrantIntentRepository>());
            })
            .SingleInstance();
        builder.Register(ctx =>
            {
                var sessionProvider = ctx.Resolve<SessionProvider>();
                return new Func<IGrantIntentStoreProvider>(() => sessionProvider.GetSessionScope().Resolve<IGrantIntentStoreProvider>());
            })
            .SingleInstance();
        builder.Register(ctx =>
            {
                var sessionProvider = ctx.Resolve<SessionProvider>();
                return new Func<IGrantIntentStore>(() => sessionProvider.GetSessionScope().Resolve<IGrantIntentStore>());
            })
            .SingleInstance();
        builder.Register(ctx =>
            {
                var sessionProvider = ctx.Resolve<SessionProvider>();
                return new Func<ISessionSaver>(() => sessionProvider.GetSessionScope().Resolve<ISessionSaver>());
            })
            .SingleInstance();
        builder.RegisterType<PathGrantSyncService>()
            .AsSelf()
            .As<IGrantSyncService>()
            .SingleInstance();
        builder.RegisterType<PathGrantService>()
            .As<IPathGrantService>()
            .As<IGrantMutatorService>()
            .As<ITraverseService>()
            .SingleInstance();
        builder.RegisterType<AclManagerScanService>()
            .As<IAclManagerScanService>()
            .SingleInstance();
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
        builder.RegisterType<AclApplyPlanBuilder>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclApplyExecutor>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclApplyPostProcessor>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclManagerApplyOrchestrator>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclImportProcessor>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclManagerExportImport>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclManagerPathActionHelper>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclManagerSelectionHandler>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclManagerMouseEventHandler>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclManagerModificationHandler>().AsSelf().InstancePerOwned<AclManagerDialog>();
        builder.RegisterType<AclDialogApplyPresenter>().AsSelf().SingleInstance();
        builder.RegisterType<AclManagerDialog>().AsSelf().InstancePerOwned<AclManagerDialog>();

        // AppEditDialog handlers — InstancePerDependency so each dialog gets its own set
        builder.RegisterType<AclAllowListGridHandler>().AsSelf().InstancePerDependency();
        builder.RegisterType<AclConfigValidator>().AsSelf().InstancePerDependency();
        builder.RegisterType<FolderDepthHelper>().AsSelf().InstancePerDependency();
        builder.RegisterType<AclConfigSection>().AsSelf().InstancePerDependency();

        if (DebugHelper.UseAdminOperationMocks)
        {
            builder.RegisterType<NonElevatedMockStore>().SingleInstance();
            builder.RegisterDecorator<MockLocalGroupQueryService, ILocalGroupQueryService>();
            builder.RegisterDecorator<MockLocalGroupMutationService, ILocalGroupMutationService>();
            builder.RegisterDecorator<MockLocalUserProvider, ILocalUserProvider>();
        }
    }
}
