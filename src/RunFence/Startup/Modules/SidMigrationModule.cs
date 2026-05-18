using Autofac;
using RunFence.Account.OrphanedProfiles;
using RunFence.SidMigration;
using RunFence.SidMigration.UI;
using RunFence.SidMigration.UI.Forms;

namespace RunFence.Startup.Modules;

public class SidMigrationModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<SidAclScanService>()
            .As<ISidAclScanService>()
            .SingleInstance();

        builder.RegisterType<SidMigrationService>()
            .As<ISidMigrationService>()
            .SingleInstance();

        builder.RegisterType<SidDeletionHandler>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<SidMigrationApplicationService>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<InAppMigrationHandler>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<ProfileSizeCalculator>()
            .As<IProfileSizeCalculator>()
            .SingleInstance();

        builder.RegisterType<RecycleBinProfileDirectoryRemovalService>()
            .As<IProfileDirectoryRemovalService>()
            .SingleInstance();

        builder.RegisterType<OrphanedProfileService>()
            .As<IOrphanedProfileService>()
            .SingleInstance();

        builder.RegisterType<OrphanedAclCleanupService>()
            .As<IOrphanedAclCleanupService>()
            .SingleInstance();

        builder.RegisterType<SidMigrationDiscoveryStepController>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<SidMigrationDiskScanStepController>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<SidMigrationDiskApplyController>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<SidMigrationWorkflowState>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<SidMigrationProgressCoordinator>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<SidMigrationStepFactory>()
            .As<ISidMigrationStepFactory>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<SidMigrationWorkflowController>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<SidMigrationWorkflowFactory>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<SidMigrationDialogFactory>()
            .AsSelf()
            .SingleInstance();
    }
}
