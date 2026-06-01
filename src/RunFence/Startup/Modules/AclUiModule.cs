using Autofac;
using RunFence.Account.UI;
using RunFence.Acl;
using RunFence.Acl.UI;
using RunFence.Groups.UI;

namespace RunFence.Startup.Modules;

public class AclUiModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<AllowListEntryFactory>().AsSelf().SingleInstance();

        builder.RegisterType<AccountAclManagerLauncher>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<GroupBulkScanOrchestrator>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountAclBulkScanService>()
            .As<IAccountAclBulkScanService>()
            .SingleInstance();

        builder.RegisterType<AclBulkScanResultProcessor>()
            .As<IAclBulkScanResultProcessor>()
            .SingleInstance();
        builder.RegisterType<AclBulkScanWorkflow>()
            .AsSelf()
            .SingleInstance();
        builder.RegisterType<AccountBulkScanHandler>()
            .As<IAccountBulkScanHandler>()
            .AsSelf()
            .SingleInstance();
        builder.RegisterType<AclBulkScanWarningPresenter>()
            .As<IAclBulkScanWarningPresenter>()
            .SingleInstance();
        builder.RegisterType<AclBulkScanMessagePresenter>()
            .As<IAclBulkScanMessagePresenter>()
            .SingleInstance();
        builder.RegisterType<AclBulkScanResultDialogFactory>()
            .As<IAclBulkScanResultDialogFactory>()
            .SingleInstance();
    }
}
