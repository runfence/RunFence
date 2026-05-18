using Moq;
using RunFence.Account;
using RunFence.Account.Lifecycle;
using RunFence.Account.UI;
using RunFence.Account.UI.AppContainer;
using RunFence.Apps;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch.Container;
using RunFence.Licensing;
using RunFence.Persistence;

namespace RunFence.Tests.Helpers;

public static class AccountContainerOrchestratorTestFactory
{
    public static AccountContainerOrchestrator Create()
    {
        var persistenceHelper = new SessionPersistenceHelper(
            new Mock<ICredentialRepository>().Object,
            new Mock<IConfigRepository>().Object,
            new Mock<ISidNameCacheService>().Object,
            () => new InlineUiThreadInvoker(action => action()),
            new Mock<ILoggingService>().Object);
        var editService = new AppContainerEditService(
            new Mock<IAppContainerService>().Object,
            new LambdaDatabaseProvider(() => new AppDatabase()),
            new Mock<ILoggingService>().Object,
            new Mock<IDatabaseService>().Object,
            new Mock<ISessionProvider>().Object);
        var enforcementHelper = new AppEntryEnforcementHelper(
            new Mock<RunFence.Acl.IAclService>().Object,
            new Mock<IShortcutService>().Object,
            new Mock<IBesideTargetShortcutService>().Object,
            new Mock<IIconService>().Object,
            new Mock<ISidNameCacheService>().Object,
            new Mock<IInteractiveUserDesktopProvider>().Object,
            new Mock<IInteractiveUserSidResolver>().Object,
            new Mock<ILoggingService>().Object);
        var cleanupHelper = new ContainerDeletionCleanupHelper(
            enforcementHelper,
            new Mock<RunFence.Acl.IAclService>().Object,
            new Mock<IIconService>().Object,
            new Mock<IShortcutDiscoveryService>().Object,
            new Mock<ILoggingService>().Object);
        var aclManagerLauncher = new AccountAclManagerLauncher(
            () => throw new NotSupportedException(),
            new Mock<ISidNameCacheService>().Object);
        var sessionProvider = new Mock<ISessionProvider>().Object;
        var messageBoxService = new Mock<IAccountMessageBoxService>().Object;
        var dialogNotifier = new Mock<IAppContainerEditDialogNotifier>().Object;
        var dialogRunner = new AppContainerEditDialogRunner(
            new Mock<IModalCoordinator>().Object,
            () => new AppContainerEditDialog(
                new AppContainerEditSubmitController(editService),
                new AppContainerDialogStateAssembler(),
                new AppContainerCapabilitiesBinder(dialogNotifier),
                new AppContainerDialogResultPresenter(dialogNotifier)),
            new Mock<ILicenseService>().Object,
            messageBoxService,
            sessionProvider);

        return new AccountContainerOrchestrator(
            persistenceHelper,
            new Mock<IContainerDeletionService>().Object,
            dialogRunner,
            aclManagerLauncher,
            cleanupHelper,
            sessionProvider,
            messageBoxService);
    }
}
