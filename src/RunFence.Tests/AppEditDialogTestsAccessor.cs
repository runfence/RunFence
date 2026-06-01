using Moq;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Acl;
using RunFence.Acl.UI;
using RunFence.Acl.UI.Forms;
using RunFence.Apps;
using RunFence.Apps.Shortcuts;
using RunFence.Apps.UI;
using RunFence.Apps.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Launching.Resolution;
using RunFence.Persistence;
using RunFence.UI;
using RunFence.UI.Forms;
using RunFence.Tests.Helpers;

namespace RunFence.Tests;

internal static class AppEditDialogTestsAccessor
{
    public static AppEditDialog CreateDialogForContextHelp(IShortcutDiscoveryService? discoveryService = null)
    {
        var appConfig = new AppConfigTestContext();

        var sidResolver = new Mock<ISidResolver>();
        sidResolver.Setup(s => s.TryResolveName(It.IsAny<string>())).Returns<string>(sid => sid);
        var profilePathResolver = new Mock<IProfilePathResolver>();
        var displayNameResolver = new SidDisplayNameResolver(sidResolver.Object, profilePathResolver.Object);

        var aclService = new Mock<IAclService>();
        aclService.Setup(s => s.IsBlockedPath(It.IsAny<string>())).Returns(false);
        aclService.Setup(s => s.ResolveAclTargetPath(It.IsAny<AppEntry>())).Returns<AppEntry>(app => app.ExePath);
        var aclConfigValidator = new AclConfigValidator(aclService.Object, Mock.Of<ILoggingService>());

        var aclSection = new AclConfigSection(
            new AclAllowListGridHandler(),
            new AllowListEntryFactory(
                Mock.Of<ILocalUserProvider>(),
                Mock.Of<ILocalGroupQueryService>(),
                Mock.Of<ISidEntryHelper>(),
                displayNameResolver),
            aclConfigValidator,
            new FolderDepthHelper(aclService.Object, Mock.Of<ILoggingService>()));

        var browseHelper = new AppEditBrowseHelper(
            discoveryService ?? Mock.Of<IShortcutDiscoveryService>(),
            Mock.Of<IShortcutIconHelper>(),
            Mock.Of<IAppDiscoveryDialogService>(),
            Mock.Of<IMessageBoxService>(),
            new ShortcutTargetResolver(Mock.Of<IShortcutGateway>()),
            new LambdaSessionProvider(() => new SessionContext
{
                Database = new AppDatabase(),
                CredentialStore = new CredentialStore(),
            }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32))),
            Mock.Of<IExecutableKindService>(),
            new AppEntryHandlerPathSuggestionService(Mock.Of<IHandlerCommandTargetReader>(), Mock.Of<IHandlerPathIconProbe>()),
            Mock.Of<IOpenFileDialogAdapterFactory>(),
            Mock.Of<IFolderBrowserDialogAdapterFactory>());

        var associationHandler = CreateAssociationHandler();
        var saveHandler = new AppEditDialogSaveHandler(
            associationHandler,
            appConfig.Service,
            Mock.Of<ILoggingService>());
        var executablePathResolver = new Mock<IExecutablePathResolver>();
        executablePathResolver.Setup(r => r.TryResolvePath(It.IsAny<string>(), It.IsAny<ExecutablePathResolutionContext>()))
            .Returns<string, ExecutablePathResolutionContext>((path, _) => path);

        var idGenerator = new Mock<IAppEntryIdGenerator>();
        idGenerator.Setup(g => g.GenerateUniqueId(It.IsAny<IEnumerable<string>>())).Returns("app1");
        var controller = new AppEditDialogController(
            new AppEntryBuilder(idGenerator.Object),
            executablePathResolver.Object,
            new AppEditDialogInputValidator(),
            new AppEditDialogAclConfigBuilder(aclConfigValidator));

        var credentialDisplayItemFactory = new CredentialDisplayItemFactory(sidResolver.Object, profilePathResolver.Object);
        var populator = new AppEditDialogPopulator(
            appConfig.Service,
            credentialDisplayItemFactory,
            new CredentialFilterHelper(sidResolver.Object));
        var initializer = new AppEditDialogInitializer(
            new AppEditPopulator(),
            idGenerator.Object,
            appConfig.Service,
            associationHandler);
        var initializationBinder = new AppEditDialogInitializationBinder(
            populator,
            initializer,
            credentialDisplayItemFactory,
            () => new IpcCallerSection(
                Mock.Of<IWindowsAccountQueryService>(service => service.GetLocalUsers() == Array.Empty<LocalUserAccount>()),
                Mock.Of<ISidEntryHelper>(),
                displayNameResolver,
                Mock.Of<IIpcCallerModalService>()));
        var submitController = new AppEditDialogSubmitController(
            controller,
            saveHandler,
            associationHandler,
            appConfig.Service,
            new AppEntryChangeClassifier());
        var programFilesProvider = new Mock<IProgramFilesPathProvider>();
        programFilesProvider.Setup(provider => provider.GetProgramFilesRoots()).Returns(Array.Empty<string>());
        var repairer = new VersionedPathRepairer(new TestBackupIntentFileSystem());
        var repairTrustPolicy = new VersionedPathAutoRepairTrustPolicy(programFilesProvider.Object, profilePathResolver.Object);
        var repairOptionsBuilder = new VersionedPathRepairOptionsBuilder(profilePathResolver.Object);
        var pathRepairSuggester = new AppEntryEditPathRepairSuggester(
            repairer,
            repairTrustPolicy,
            repairOptionsBuilder,
            Mock.Of<IMessageBoxService>());

        return new AppEditDialog(
            appConfig.Service,
            aclSection,
            browseHelper,
            new AppEditAccountSwitchHandler(),
            submitController,
            Mock.Of<ILoggingService>(),
            executablePathResolver.Object,
            new HandlerAssociationsSection(),
            initializationBinder,
            Mock.Of<IUserConfirmationService>(service => service.Confirm(It.IsAny<string>(), It.IsAny<string>()) == true),
            Mock.Of<IHandlerAssociationMutationService>(),
            new HandlerAssociationsChildDialogCoordinator(
                () => new HandlerAssociationEditDialog(),
                Mock.Of<IExeAssociationRegistryReader>(),
                Mock.Of<IMessageBoxService>(),
                Mock.Of<IModalCoordinator>()),
            Mock.Of<IUiIconService>(),
            new AppEditDialogSnapshotProvider(),
            pathRepairSuggester);
    }

    public static AppEditDialogCommandContext CreateNoOpCommandContext()
        => new(_ => Task.CompletedTask);

    private static AppEditAssociationHandler CreateAssociationHandler()
    {
        var database = new AppDatabase();
        var handlerMappingService = new Mock<IHandlerMappingService>();
        handlerMappingService.Setup(s => s.GetAllHandlerMappings(database))
            .Returns(new Dictionary<string, IReadOnlyList<HandlerMappingEntry>>(StringComparer.OrdinalIgnoreCase));
        handlerMappingService.Setup(s => s.GetEffectiveHandlerMappings(database))
            .Returns(new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase));

        var databaseProvider = new LambdaDatabaseProvider(() => database);
        return new AppEditAssociationHandler(
            handlerMappingService.Object,
            Mock.Of<IAppHandlerRegistrationService>(),
            Mock.Of<IAssociationAutoSetService>(),
            databaseProvider,
            () => new HandlerMappingMutationHandler(
                handlerMappingService.Object));
    }

    private sealed class TestBackupIntentFileSystem : IBackupIntentFileSystem
    {
        public BackupIntentPathState GetFileState(string path) => BackupIntentPathState.Exists;

        public BackupIntentPathState GetDirectoryState(string path) => BackupIntentPathState.Exists;

        public bool TryEnumerateDirectories(string path, out IReadOnlyList<string> directories)
        {
            directories = [];
            return true;
        }

        public bool TryGetDirectoryLastWriteTimeUtc(string path, out DateTime lastWriteTimeUtc)
        {
            lastWriteTimeUtc = default;
            return false;
        }
    }
}
