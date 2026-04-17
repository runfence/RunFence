using Autofac;
using Autofac.Extras.Ordering;
using RunFence.Account;
using RunFence.Account.Lifecycle;
using RunFence.Account.UI;
using RunFence.Account.UI.AppContainer;
using RunFence.Account.UI.Forms;
using RunFence.Acl.Traverse;
using RunFence.Infrastructure;
using RunFence.SidMigration;

namespace RunFence.Startup.Modules;

public class AccountModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<AccountPasswordService>()
            .As<IAccountPasswordService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<WindowsAccountService>()
            .As<IWindowsAccountService>()
            .SingleInstance();

        builder.RegisterType<SystemDialogLauncher>()
            .As<ISystemDialogLauncher>()
            .SingleInstance();

        builder.RegisterType<CredentialFilterHelper>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountLoginRestrictionService>()
            .As<IAccountLoginRestrictionService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountLsaRestrictionService>()
            .As<IAccountLsaRestrictionService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<LogonScriptIniManager>().AsSelf().SingleInstance();

        builder.RegisterType<GroupPolicyScriptHelper>()
            .As<IGroupPolicyScriptHelper>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountValidationService>()
            .As<IAccountValidationService>()
            .SingleInstance();

        builder.RegisterType<AccountCredentialManager>()
            .As<IAccountCredentialManager>()
            .SingleInstance();

        builder.RegisterType<SessionPersistenceHelper>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<SessionSaver>()
            .As<ISessionSaver>()
            .SingleInstance();

        builder.RegisterType<GrantReconciliationService>().AsSelf().SingleInstance();

        builder.RegisterType<AccountLifecycleManager>()
            .As<IAccountLifecycleManager>()
            .SingleInstance();

        builder.RegisterType<AccountDeletionService>()
            .As<IAccountDeletionService>()
            .SingleInstance();

        builder.RegisterType<ContainerDeletionService>()
            .As<IContainerDeletionService>()
            .SingleInstance();

        builder.RegisterType<EphemeralAccountService>()
            .AsSelf()
            .As<IBackgroundService>()
            .OrderBy(0)
            .SingleInstance();

        builder.RegisterType<EphemeralContainerService>()
            .AsSelf()
            .As<IBackgroundService>()
            .OrderBy(1)
            .SingleInstance();

        builder.RegisterType<AccountImportHandler>()
            .As<IAccountImportHandler>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<SidCleanupHelper>()
            .As<ISidCleanupHelper>()
            .SingleInstance();

        // Account panel handlers
        builder.RegisterType<AccountSidResolutionService>()
            .As<IAccountSidResolutionService>()
            .SingleInstance();

        builder.RegisterType<AccountToggleService>()
            .As<IAccountToggleService>()
            .SingleInstance();

        builder.RegisterType<AccountGridSupplementarySections>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountGridPopulator>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<ReconciliationGuard>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountGridRefreshHandler>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountPanelActions>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountsPanelRefreshOrchestrator>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountContextMenuHandler>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<ContainerContextMenuHandler>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountFirewallMenuHandler>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountProcessMenuHandler>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountContextMenuOrchestrator>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountsPanelGridInteraction>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountToolResolver>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<PackageInstallService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<ToolLauncher>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountTrayToggleService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountsPanelTimerCoordinator>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountSidMigrationLauncher>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountEditHelper>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountCredentialOperations>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountsPanelCredentialHandler>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountPostCreateSetupService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountCreationCommitService>()
            .As<IAccountCreationCommitService>()
            .SingleInstance();

        builder.RegisterType<AccountCreationOrchestrator>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountDeletionOrchestrator>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountProcessTimerManager>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountGridProcessExpander>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountProcessRowPainter>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountProcessDisplayManager>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountImportUIHandler>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountGridEditHandler>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountGridTypeAheadHandler>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<PasswordAutoTyper>()
            .As<IPasswordAutoTyper>()
            .SingleInstance();

        builder.RegisterType<SecureClipboardService>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<AccountPasswordHandler>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AppContainerEditService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<ContainerDeletionCleanupHelper>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<EditAccountDialogSaveHandler>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<EditAccountDialog>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<CredentialEditDialog>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<SidNameCacheService>()
            .As<ISidNameCacheService>()
            .SingleInstance();
    }
}