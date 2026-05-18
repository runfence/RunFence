using Autofac;
using Autofac.Extras.Ordering;
using RunFence.Account;
using RunFence.Account.Lifecycle;
using RunFence.Account.UI;
using RunFence.Account.UI.AppContainer;
using RunFence.Account.UI.Forms;
using RunFence.Acl.Traverse;
using RunFence.Core;
using RunFence.Groups;
using RunFence.Infrastructure;
using RunFence.SidMigration;
using RunFence.Startup;
using RunFence.Startup.NonElevatedMocks;
using RunFence.Wizard;

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

        builder.RegisterType<WindowsAccountQueryService>()
            .As<IWindowsAccountQueryService>()
            .SingleInstance();

        builder.RegisterType<LocalAccountProvisioningService>()
            .As<ILocalAccountProvisioningService>()
            .SingleInstance();

        builder.RegisterType<SystemDialogLauncher>()
            .As<ISystemDialogLauncher>()
            .SingleInstance();

        builder.RegisterType<CredentialFilterHelper>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<CredentialDisplayItemFactory>()
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

        builder.RegisterType<AccountRestrictionCoordinator>()
            .As<IAccountRestrictionCoordinator>()
            .SingleInstance();

        builder.RegisterType<LogonScriptIniManager>().AsSelf().SingleInstance();

        builder.RegisterType<GroupPolicyScriptHelper>()
            .As<IGroupPolicyScriptHelper>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<GroupDeletionService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountValidationService>()
            .As<IAccountValidationService>()
            .SingleInstance();

        builder.RegisterType<AccountCredentialManager>()
            .As<IAccountCredentialManager>()
            .SingleInstance();

        builder.RegisterType<ValidationRunner>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<SessionPersistenceHelper>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<SessionSaver>()
            .As<ISessionSaver>()
            .SingleInstance();

        builder.RegisterType<SidReconciler>().AsSelf().SingleInstance();

        builder.RegisterType<GrantReconciliationService>().AsSelf().SingleInstance();

        builder.RegisterType<AccountGrantReconciliationRunner>()
            .As<IAccountGrantReconciliationRunner>()
            .SingleInstance();

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
            .As<IEphemeralAccountChangeSource>()
            .As<IBackgroundService>()
            .OrderBy(0)
            .SingleInstance();

        builder.RegisterType<EphemeralContainerService>()
            .AsSelf()
            .As<IEphemeralContainerChangeSource>()
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
            .As<IAccountAppsTextProvider>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountGridSorter>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountGridIconLifetimeManager>()
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

        builder.RegisterType<AccountMenuStateConfigurator>()
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

        builder.RegisterType<ProcessCommandLineFormatter>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<ProcessRowGridUpdater>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<AccountContextMenuOrchestrator>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountsPanelGridInteraction>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountToolResolver>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<PackageInstallScriptStore>()
            .As<IPackageInstallScriptStore>()
            .SingleInstance();

        builder.RegisterType<PackageInstallLauncher>()
            .As<IPackageInstallLauncher>()
            .SingleInstance();

        builder.RegisterType<PackageInstallService>()
            .As<IPackageInstallService>()
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

        builder.RegisterType<CredentialDialogHelper>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountCredentialCrudHandler>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountEditOrchestrator>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountsPanelCredentialHandler>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountPostCreateSetupService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountCreationProgressRunner>()
            .As<IAccountCreationProgressRunner>()
            .SingleInstance();

        builder.RegisterType<AccountCreationCommitService>()
            .As<IAccountCreationCommitService>()
            .SingleInstance();

        builder.RegisterType<CreatedAccountRollbackExecutor>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountCreationRollbackService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountCreationOrchestrator>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountDeletionPreflightService>()
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
            .As<ISecureClipboardService>()
            .InstancePerDependency();

        builder.RegisterType<AccountPasswordAccessHandler>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AccountPasswordMutationHandler>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AppContainerEditService>()
            .As<IAppContainerEditService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<ContainerDeletionCleanupHelper>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<EditAccountDialogSaveHandler>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<EditAccountDialog>()
            .As<IAccountCreationDialog>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<CredentialEditDialog>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<CredentialDialogRunner>()
            .As<ICredentialDialogRunner>()
            .InstancePerDependency();

        builder.RegisterType<SidNameCacheService>()
            .As<ISidNameCacheService>()
            .SingleInstance();

        builder.RegisterType<AccountConfigMigrationService>()
            .As<IAccountConfigMigrationService>()
            .SingleInstance();

        if (DebugHelper.UseAdminOperationMocks)
        {
            builder.RegisterDecorator<MockWindowsAccountService, IWindowsAccountService>();
            builder.RegisterDecorator<NoOpAccountLoginRestrictionService, IAccountLoginRestrictionService>();
            builder.RegisterDecorator<NoOpGroupPolicyScriptHelper, IGroupPolicyScriptHelper>();
        }
    }
}
