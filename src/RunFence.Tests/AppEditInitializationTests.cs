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
using RunFence.Tests.Helpers;
using RunFence.UI;
using RunFence.UI.Forms;
using Xunit;

namespace RunFence.Tests;

public class AppEditInitializationTests
{
    private const string AppId = "app01";
    private const string AccountSid = "S-1-5-21-1-2-3-1001";
    private const string CallerSid = "S-1-5-21-1-2-3-1002";
    private const string ExtraConfigPath = @"C:\configs\extra.rfn";

    [Fact]
    public void CreateExistingInitializationModel_FiltersAndMapsMatchingAssociations()
    {
        var app = CreateExistingApp();
        var database = new AppDatabase();
        var initializer = CreateInitializer(database, out var appConfigService, out var handlerMappingService);
        appConfigService.Setup(s => s.GetConfigPath(AppId)).Returns(ExtraConfigPath);
        handlerMappingService.Setup(s => s.GetAllHandlerMappings(database)).Returns(
            new Dictionary<string, IReadOnlyList<HandlerMappingEntry>>(StringComparer.OrdinalIgnoreCase)
            {
                [".txt"] = [new HandlerMappingEntry(AppId, "\"%1\"", ["C:\\"], true)],
                [".jpg"] = [new HandlerMappingEntry("other")]
            });

        var model = initializer.CreateExistingInitializationModel(app, database);

        Assert.Equal("Existing App", model.State.Name);
        Assert.Equal(app.ExePath, model.State.ExePath);
        Assert.True(model.AclState.RestrictAcl);
        Assert.Equal(AclMode.Deny, model.AclState.AclMode);
        Assert.Equal(DeniedRights.Execute, model.AclState.DeniedRights);
        Assert.Equal(AclTarget.Folder, model.AclState.AclTarget);
        Assert.Equal(0, model.AclState.FolderAclDepth);
        Assert.Equal(PrivilegeLevel.Basic, model.State.SelectedPrivilegeLevel);
        Assert.True(model.State.OverrideIpcCallers);
        Assert.Equal(AccountSid, model.AccountSelection.AccountSid);
        Assert.False(model.AccountSelection.IsAppContainer);
        Assert.Equal(ExtraConfigPath, model.SelectedConfigPath);
        Assert.Equal([CallerSid], model.IpcCallers);
        Assert.Equal("value", model.EnvironmentVariables!["RF_TEST"]);
        var association = Assert.Single(model.Associations!);
        Assert.Equal(".txt", association.Key);
        Assert.Equal("\"%1\"", association.ArgumentsTemplate);
        Assert.Equal(["C:\\"], association.PathPrefixes);
        Assert.True(association.ReplacePrefixes);
        Assert.Equal(["C:\\Allowed"], model.PathPrefixes);
    }

    [Fact]
    public void AppEditDialog_ApplyExistingInitialization_NormalizesAppPathPrefixes()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var app = CreateExistingApp();
            var database = new AppDatabase();
            database.Apps.Add(app);
            var snapshotProvider = new AppEditDialogSnapshotProvider();
            using var dialog = CreateDialog(database, out var appConfigService, out var handlerMappingService);
            appConfigService.Setup(s => s.HasLoadedConfigs).Returns(true);
            appConfigService.Setup(s => s.GetLoadedConfigPaths()).Returns([ExtraConfigPath]);
            appConfigService.Setup(s => s.GetConfigPath(AppId)).Returns(ExtraConfigPath);
            handlerMappingService.Setup(s => s.GetAllHandlerMappings(database)).Returns(
                new Dictionary<string, IReadOnlyList<HandlerMappingEntry>>(StringComparer.OrdinalIgnoreCase)
                {
                    [".txt"] = [new HandlerMappingEntry(AppId, "\"%1\"")]
                });

            dialog.Initialize(
                existing: app,
                credentials: [new CredentialEntry { Sid = AccountSid }],
                existingApps: [app],
                commandContext: AppEditDialogTestsAccessor.CreateNoOpCommandContext(),
                sidNames: new Dictionary<string, string> { [AccountSid] = "Test User" },
                database: database);

            var snapshot = snapshotProvider.CaptureInputSnapshot(dialog, dialog);
            Assert.Equal("Existing App", snapshot.NameText);
            Assert.Equal(app.ExePath, snapshot.FilePathText);
            Assert.False(snapshot.IsFolder);
            Assert.Equal("--default", snapshot.DefaultArgsText);
            Assert.True(snapshot.AllowPassArgs);
            Assert.Equal("\"%1\"", snapshot.ArgumentsTemplateText);
            Assert.Equal(@"C:\Work", snapshot.WorkingDirText);
            Assert.True(snapshot.AllowPassWorkDir);
            Assert.True(snapshot.ManageShortcuts);
            Assert.Equal(PrivilegeLevel.Basic, snapshot.SelectedPrivilegeLevel);
            Assert.True(snapshot.OverrideIpcCallers);
            Assert.Equal(["C:\\Allowed\\"], snapshot.AppPathPrefixes);
            Assert.Equal(ExtraConfigPath, dialog.SelectedConfigPath);
            Assert.Equal(AccountSid, snapshot.SelectedAccountSid);
        });
    }

    [Fact]
    public void AppEditDialog_InitializeExistingApp_RepairSuggestionUpdatesOnlyEditControls()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var app = CreateExistingApp();
            var database = new AppDatabase();
            database.Apps.Add(app);
            var snapshotProvider = new AppEditDialogSnapshotProvider();
            var messageBox = new Mock<IMessageBoxService>();
            messageBox.Setup(service => service.Show(
                    It.Is<string>(text =>
                        text.Contains(@"D:\Apps\Slack\app-4.50.121\Slack.exe", StringComparison.Ordinal) &&
                        text.Contains(@"D:\Apps\Slack\app-4.51.0\Slack.exe", StringComparison.Ordinal)),
                    "RunFence",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question))
                .Returns(DialogResult.Yes);
            using var dialog = CreateDialog(
                database,
                out var appConfigService,
                out var handlerMappingService,
                backupIntentFileSystem: new MissingPathBackupIntentFileSystem()
                    .WithMissingFile(app.ExePath)
                    .WithExistingDirectory(@"D:\Apps\Slack")
                    .WithEnumeratedDirectories(@"D:\Apps\Slack", [@"D:\Apps\Slack\app-4.51.0"])
                    .WithExistingFile(@"D:\Apps\Slack\app-4.51.0\Slack.exe"),
                messageBoxService: messageBox.Object);
            app.ExePath = @"D:\Apps\Slack\app-4.50.121\Slack.exe";
            appConfigService.Setup(s => s.HasLoadedConfigs).Returns(true);
            appConfigService.Setup(s => s.GetLoadedConfigPaths()).Returns([ExtraConfigPath]);
            appConfigService.Setup(s => s.GetConfigPath(AppId)).Returns(ExtraConfigPath);
            handlerMappingService.Setup(s => s.GetAllHandlerMappings(database)).Returns(
                new Dictionary<string, IReadOnlyList<HandlerMappingEntry>>(StringComparer.OrdinalIgnoreCase)
                {
                    [".txt"] = [new HandlerMappingEntry(AppId, "\"%1\"")]
                });

            dialog.Initialize(
                existing: app,
                credentials: [new CredentialEntry { Sid = AccountSid }],
                existingApps: [app],
                commandContext: AppEditDialogTestsAccessor.CreateNoOpCommandContext(),
                sidNames: new Dictionary<string, string> { [AccountSid] = "Test User" },
                database: database);

            var snapshot = snapshotProvider.CaptureInputSnapshot(dialog, dialog);
            Assert.Equal(@"D:\Apps\Slack\app-4.51.0\Slack.exe", snapshot.FilePathText);
            Assert.Equal(@"D:\Apps\Slack\app-4.50.121\Slack.exe", database.Apps.Single().ExePath);
            Assert.Equal(ExtraConfigPath, dialog.SelectedConfigPath);
            messageBox.Verify(service => service.Show(
                It.IsAny<string>(),
                "RunFence",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question), Times.Once);
        });
    }

    [Fact]
    public void AppEditDialog_InitializeNewContainerApp_PreservesRequestedPrivilegeLevel()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var database = new AppDatabase();
            database.AppContainers.Add(new AppContainerEntry
            {
                Name = "ram_browser",
                DisplayName = "Browser",
                Sid = "S-1-15-2-1"
            });
            var snapshotProvider = new AppEditDialogSnapshotProvider();
            using var dialog = CreateDialog(database, out var appConfigService, out _);
            appConfigService.Setup(s => s.HasLoadedConfigs).Returns(false);
            appConfigService.Setup(s => s.GetLoadedConfigPaths()).Returns([]);

            dialog.Initialize(
                existing: null,
                credentials: [],
                existingApps: [],
                commandContext: AppEditDialogTestsAccessor.CreateNoOpCommandContext(),
                options: new AppEditDialogOptions(
                    ExePath: @"C:\Apps\Browser.exe",
                    ContainerName: "ram_browser",
                    PrivilegeLevel: PrivilegeLevel.Isolated),
                database: database);

            var snapshot = snapshotProvider.CaptureInputSnapshot(dialog, dialog);
            Assert.Equal("ram_browser", snapshot.SelectedAppContainerName);
            Assert.Null(snapshot.SelectedAccountSid);
            Assert.Equal(PrivilegeLevel.LowIntegrity, snapshot.SelectedPrivilegeLevel);
            Assert.Equal(PrivilegeLevel.Isolated, snapshot.PersistedPrivilegeLevel);
        });
    }

    [Fact]
    public void AppEditDialog_InitializeExistingContainerApp_PreservesStoredPrivilegeLevel()
    {
        StaTestHelper.RunOnSta(() =>
        {
            var app = CreateExistingApp();
            app.AccountSid = string.Empty;
            app.AppContainerName = "ram_browser";

            var database = new AppDatabase();
            database.Apps.Add(app);
            database.AppContainers.Add(new AppContainerEntry
            {
                Name = "ram_browser",
                DisplayName = "Browser",
                Sid = "S-1-15-2-1"
            });

            var snapshotProvider = new AppEditDialogSnapshotProvider();
            using var dialog = CreateDialog(database, out var appConfigService, out var handlerMappingService);
            appConfigService.Setup(s => s.HasLoadedConfigs).Returns(true);
            appConfigService.Setup(s => s.GetLoadedConfigPaths()).Returns([ExtraConfigPath]);
            appConfigService.Setup(s => s.GetConfigPath(AppId)).Returns(ExtraConfigPath);
            handlerMappingService.Setup(s => s.GetAllHandlerMappings(database)).Returns(
                new Dictionary<string, IReadOnlyList<HandlerMappingEntry>>(StringComparer.OrdinalIgnoreCase));

            dialog.Initialize(
                existing: app,
                credentials: [],
                existingApps: [app],
                commandContext: AppEditDialogTestsAccessor.CreateNoOpCommandContext(),
                database: database);

            var snapshot = snapshotProvider.CaptureInputSnapshot(dialog, dialog);
            Assert.Equal("ram_browser", snapshot.SelectedAppContainerName);
            Assert.Null(snapshot.SelectedAccountSid);
            Assert.Equal(PrivilegeLevel.LowIntegrity, snapshot.SelectedPrivilegeLevel);
            Assert.Equal(PrivilegeLevel.Basic, snapshot.PersistedPrivilegeLevel);
        });
    }

    private static AppEntry CreateExistingApp() => new()
    {
        Id = AppId,
        Name = "Existing App",
        ExePath = @"C:\Missing\app.exe",
        AccountSid = AccountSid,
        DefaultArguments = "--default",
        AllowPassingArguments = true,
        ArgumentsTemplate = "\"%1\"",
        WorkingDirectory = @"C:\Work",
        AllowPassingWorkingDirectory = true,
        ManageShortcuts = true,
        PrivilegeLevel = PrivilegeLevel.Basic,
        AllowedIpcCallers = [CallerSid],
        EnvironmentVariables = new Dictionary<string, string> { ["RF_TEST"] = "value" },
        PathPrefixes = ["C:\\Allowed"],
        RestrictAcl = true,
        AclMode = AclMode.Deny,
        AclTarget = AclTarget.Folder,
        DeniedRights = DeniedRights.Execute,
        FolderAclDepth = 0
    };

    private static AppEditDialogInitializer CreateInitializer(
        AppDatabase database,
        out Mock<IAppConfigService> appConfigService,
        out Mock<IHandlerMappingService> handlerMappingService)
    {
        appConfigService = new Mock<IAppConfigService>();
        handlerMappingService = new Mock<IHandlerMappingService>();
        handlerMappingService.Setup(s => s.GetAllHandlerMappings(database))
            .Returns(new Dictionary<string, IReadOnlyList<HandlerMappingEntry>>(StringComparer.OrdinalIgnoreCase));
        var associationHandler = CreateAssociationHandler(database, handlerMappingService.Object);

        return new AppEditDialogInitializer(
            new AppEditPopulator(),
            new AppEntryIdGenerator(),
            appConfigService.Object,
            associationHandler);
    }

    private static AppEditDialog CreateDialog(
        AppDatabase database,
        out Mock<IAppConfigService> appConfigService,
        out Mock<IHandlerMappingService> handlerMappingService,
        IBackupIntentFileSystem? backupIntentFileSystem = null,
        IMessageBoxService? messageBoxService = null)
    {
        var sidResolver = CreateSidResolver();
        var profilePathResolver = new Mock<IProfilePathResolver>();
        var initializer = CreateInitializer(database, out appConfigService, out handlerMappingService);
        var associationHandler = CreateAssociationHandler(database, handlerMappingService.Object);
        var aclService = new Mock<IAclService>();
        aclService.Setup(s => s.ResolveAclTargetPath(It.IsAny<AppEntry>()))
            .Returns((AppEntry app) => app.ExePath);
        aclService.Setup(s => s.IsBlockedPath(It.IsAny<string>())).Returns(false);
        var log = new Mock<ILoggingService>();
        var displayNameResolver = new SidDisplayNameResolver(sidResolver.Object, profilePathResolver.Object);
        var aclConfigValidator = new AclConfigValidator(aclService.Object, log.Object);
        var aclSection = new AclConfigSection(
            new AclAllowListGridHandler(),
            new AllowListEntryFactory(
                new Mock<ILocalUserProvider>().Object,
                new Mock<ILocalGroupQueryService>().Object,
                new Mock<ISidEntryHelper>().Object,
                displayNameResolver),
            aclConfigValidator,
            new FolderDepthHelper(aclService.Object, log.Object));
        var executablePathResolver = new Mock<IExecutablePathResolver>();
        executablePathResolver.Setup(r => r.TryResolvePath(It.IsAny<string>(), It.IsAny<ExecutablePathResolutionContext>()))
            .Returns((string path, ExecutablePathResolutionContext _) => path);
        var browseHelper = new AppEditBrowseHelper(
            new Mock<IShortcutDiscoveryService>().Object,
            new Mock<IShortcutIconHelper>().Object,
            new Mock<IAppDiscoveryDialogService>().Object,
            messageBoxService ?? new Mock<IMessageBoxService>().Object,
            new ShortcutTargetResolver(new Mock<IShortcutGateway>().Object),
            new Mock<ISessionProvider>().Object,
            new Mock<IExecutableKindService>().Object,
            new AppEntryHandlerPathSuggestionService(Mock.Of<IHandlerCommandTargetReader>(), Mock.Of<IHandlerPathIconProbe>()),
            Mock.Of<IOpenFileDialogAdapterFactory>(),
            Mock.Of<IFolderBrowserDialogAdapterFactory>());
        var controller = new AppEditDialogController(
            new AppEntryBuilder(new AppEntryIdGenerator()),
            executablePathResolver.Object,
            new AppEditDialogInputValidator(),
            new AppEditDialogAclConfigBuilder(aclConfigValidator));
        var saveHandler = new AppEditDialogSaveHandler(
            associationHandler,
            appConfigService.Object,
            Mock.Of<ILoggingService>());
        var submitController = new AppEditDialogSubmitController(
            controller,
            saveHandler,
            associationHandler,
            appConfigService.Object,
            new AppEntryChangeClassifier());
        var credentialDisplayItemFactory = new CredentialDisplayItemFactory(sidResolver.Object, profilePathResolver.Object);
        var populator = new AppEditDialogPopulator(
            appConfigService.Object,
            credentialDisplayItemFactory,
            new CredentialFilterHelper(sidResolver.Object));
        var binder = new AppEditDialogInitializationBinder(
            populator,
            initializer,
            credentialDisplayItemFactory,
            () => new IpcCallerSection(
                Mock.Of<IWindowsAccountQueryService>(service => service.GetLocalUsers() == Array.Empty<LocalUserAccount>()),
                new Mock<ISidEntryHelper>().Object,
                new SidDisplayNameResolver(sidResolver.Object, profilePathResolver.Object),
                Mock.Of<IIpcCallerModalService>()));
        var programFilesProvider = new Mock<IProgramFilesPathProvider>();
        programFilesProvider.Setup(provider => provider.GetProgramFilesRoots()).Returns([]);
        var pathRepairSuggester = new AppEntryEditPathRepairSuggester(
            new VersionedPathRepairer(backupIntentFileSystem ?? new ExistingBackupIntentFileSystem()),
            new VersionedPathAutoRepairTrustPolicy(programFilesProvider.Object, profilePathResolver.Object),
            new VersionedPathRepairOptionsBuilder(profilePathResolver.Object),
            messageBoxService ?? Mock.Of<IMessageBoxService>());

        return new AppEditDialog(
            appConfigService.Object,
            aclSection,
            browseHelper,
            new AppEditAccountSwitchHandler(),
            submitController,
            Mock.Of<ILoggingService>(),
            executablePathResolver.Object,
            new HandlerAssociationsSection(),
            binder,
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

    private static AppEditAssociationHandler CreateAssociationHandler(
        AppDatabase database,
        IHandlerMappingService handlerMappingService)
    {
        var databaseProvider = new LambdaDatabaseProvider(() => database);
        return new AppEditAssociationHandler(
            handlerMappingService,
            new Mock<IAppHandlerRegistrationService>().Object,
            new Mock<IAssociationAutoSetService>().Object,
            databaseProvider,
            () => new HandlerMappingMutationHandler(
                handlerMappingService));
    }

    private static Mock<ISidResolver> CreateSidResolver()
    {
        var sidResolver = new Mock<ISidResolver>();
        sidResolver.Setup(r => r.TryResolveName(AccountSid)).Returns("Test User");
        sidResolver.Setup(r => r.TryResolveName(CallerSid)).Returns("Caller User");
        sidResolver.Setup(r => r.GetCurrentUserSid()).Returns("S-1-5-21-current");
        return sidResolver;
    }

    private sealed class ExistingBackupIntentFileSystem : IBackupIntentFileSystem
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

    private sealed class MissingPathBackupIntentFileSystem : IBackupIntentFileSystem
    {
        private readonly Dictionary<string, BackupIntentPathState> _fileStates = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, BackupIntentPathState> _directoryStates = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IReadOnlyList<string>> _enumerations = new(StringComparer.OrdinalIgnoreCase);

        public MissingPathBackupIntentFileSystem WithMissingFile(string path)
        {
            _fileStates[Normalize(path)] = BackupIntentPathState.Missing;
            return this;
        }

        public MissingPathBackupIntentFileSystem WithExistingFile(string path)
        {
            _fileStates[Normalize(path)] = BackupIntentPathState.Exists;
            return this;
        }

        public MissingPathBackupIntentFileSystem WithExistingDirectory(string path)
        {
            _directoryStates[Normalize(path)] = BackupIntentPathState.Exists;
            return this;
        }

        public MissingPathBackupIntentFileSystem WithEnumeratedDirectories(string path, IReadOnlyList<string> directories)
        {
            _enumerations[Normalize(path)] = directories.Select(Normalize).ToArray();
            return this;
        }

        public BackupIntentPathState GetFileState(string path)
            => _fileStates.GetValueOrDefault(Normalize(path), BackupIntentPathState.Missing);

        public BackupIntentPathState GetDirectoryState(string path)
            => _directoryStates.GetValueOrDefault(Normalize(path), BackupIntentPathState.Missing);

        public bool TryEnumerateDirectories(string path, out IReadOnlyList<string> directories)
        {
            directories = _enumerations.GetValueOrDefault(Normalize(path), []);
            return true;
        }

        public bool TryGetDirectoryLastWriteTimeUtc(string path, out DateTime lastWriteTimeUtc)
        {
            lastWriteTimeUtc = default;
            return false;
        }

        private static string Normalize(string path) => Path.GetFullPath(path);
    }
}
