using Autofac;
using RunFence.Account.OrphanedProfiles;
using RunFence.SidMigration;

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

        builder.RegisterType<InAppMigrationHandler>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<OrphanedProfileService>()
            .As<IOrphanedProfileService>()
            .SingleInstance();

        builder.RegisterType<OrphanedAclCleanupService>()
            .As<IOrphanedAclCleanupService>()
            .SingleInstance();
    }
}