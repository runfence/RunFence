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

namespace RunFence.Tests;

internal static class AppEditDialogTestsAccessor
{
    public static AppEditDialog CreateDialogForContextHelp(IShortcutDiscoveryService? discoveryService = null)
    {
        var appConfig = CreateAppConfigService();

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
                Mock.Of<ILocalGroupMembershipService>(),
                Mock.Of<ISidEntryHelper>(),
                displayNameResolver),
            aclConfigValidator,
            new FolderDepthHelper(aclService.Object, Mock.Of<ILoggingService>()));

        var browseHelper = new AppEditBrowseHelper(
            discoveryService ?? Mock.Of<IShortcutDiscoveryService>(),
            Mock.Of<IShortcutIconHelper>(),
            new ShortcutTargetResolver(Mock.Of<IShortcutComHelper>()),
            new LambdaSessionProvider(() => new SessionContext
{
                Database = new AppDatabase(),
                CredentialStore = new CredentialStore(),
            }.WithOwnedPinDerivedKey(TestSecretFactory.Create(32))),
            Mock.Of<IExecutableKindService>());

        var associationHandler = CreateAssociationHandler();
        var saveHandler = new AppEditDialogSaveHandler(associationHandler, appConfig);
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
            appConfig,
            credentialDisplayItemFactory,
            new CredentialFilterHelper(sidResolver.Object));
        var initializer = new AppEditDialogInitializer(
            new AppEditPopulator(),
            idGenerator.Object,
            appConfig,
            associationHandler);
        var initializationBinder = new AppEditDialogInitializationBinder(
            populator,
            initializer,
            credentialDisplayItemFactory,
            () => new IpcCallerSection(
                () => [],
                Mock.Of<ISidEntryHelper>(),
                displayNameResolver));
        var submitController = new AppEditDialogSubmitController(
            controller,
            saveHandler);

        return new AppEditDialog(
            appConfig,
            aclSection,
            browseHelper,
            new AppEditAccountSwitchHandler(),
            controller,
            submitController,
            executablePathResolver.Object,
            new HandlerAssociationsSection(),
            initializationBinder,
            Mock.Of<IUserConfirmationService>(service => service.Confirm(It.IsAny<string>(), It.IsAny<string>()) == true));
    }

    public static AppEditDialogCommandContext CreateNoOpCommandContext()
        => new(() => Task.CompletedTask);

    private static IAppConfigService CreateAppConfigService()
    {
        var appConfig = new Mock<IAppConfigService>();
        appConfig.SetupGet(s => s.HasLoadedConfigs).Returns(false);
        appConfig.Setup(s => s.GetLoadedConfigPaths()).Returns([]);
        return appConfig.Object;
    }

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
}
