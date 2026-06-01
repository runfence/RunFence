using Moq;
using RunFence.Acl;
using RunFence.Acl.Permissions;
using RunFence.Acl.Traverse;
using RunFence.Account;
using RunFence.Account.Lifecycle;
using RunFence.Account.UI;
using RunFence.Apps;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Launch.Container;
using RunFence.Licensing;
using RunFence.Persistence;
using RunFence.Startup;
using RunFence.Startup.UI;
using RunFence.Wizard.UI;
using Xunit;

namespace RunFence.Tests;

public sealed class DeferredStartupRunnerTests
{
    private readonly AppDatabase _database = new();
    private readonly Mock<IAppConfigService> _appConfigService = new();
    private readonly Mock<IAclService> _aclService = new();
    private readonly Mock<IShortcutService> _shortcutService = new();
    private readonly Mock<IBesideTargetShortcutService> _besideTargetShortcutService = new();
    private readonly Mock<IIconService> _iconService = new();
    private readonly Mock<ISidNameCacheService> _sidNameCache = new();
    private readonly Mock<IInteractiveUserDesktopProvider> _desktopProvider = new();
    private readonly Mock<IInteractiveUserSidResolver> _interactiveUserSidResolver = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<ISessionSaver> _sessionSaver = new();
    private readonly Mock<IStartupRepairWarningPresenter> _startupRepairWarningPresenter = new();

    public DeferredStartupRunnerTests()
    {
        _iconService.Setup(service => service.CreateBadgedIcon(It.IsAny<AppEntry>())).Returns("icon.ico");
        _sidNameCache.Setup(cache => cache.GetDisplayName(It.IsAny<string>())).Returns<string>(sid => sid);
    }

    [Fact]
    public void PrepareStartupSnapshot_PathRepairPersistsBeforeSnapshotCreation()
    {
        var app = new AppEntry
        {
            Id = "pf010",
            ExePath = @"C:\Program Files\Vendor\App-1.0\App.exe"
        };
        _database.Apps = [app];

        using var session = new SessionContext
        {
            Database = _database,
            CredentialStore = new CredentialStore()
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));
        var sessionProvider = new LambdaSessionProvider(() => session);
        var folderHandlerService = CreateFolderHandlerServiceMock();
        var startupRunner = CreateStartupRunner(
            sessionProvider,
            new TestBackupIntentFileSystem()
                .WithMissingFile(app.ExePath)
                .WithExistingDirectory(@"C:\Program Files\Vendor")
                .WithEnumeratedDirectories(@"C:\Program Files\Vendor", [@"C:\Program Files\Vendor\App-1.1"])
                .WithExistingFile(@"C:\Program Files\Vendor\App-1.1\App.exe"),
            programFilesRoots: [@"C:\Program Files"]);
        var deferredRunner = new DeferredStartupRunner(
            folderHandlerService.Object,
            CreateFeatureActivator(sessionProvider),
            startupRunner,
            sessionProvider,
            _sessionSaver.Object,
            new Mock<IPackageInstallService>().Object,
            _startupRepairWarningPresenter.Object,
            new StartupOptions(IsBackground: false, PinBypassed: false),
            _log.Object);

        var result = deferredRunner.PrepareStartupSnapshot();

        Assert.Null(result.SaveFailureMessage);
        Assert.NotNull(result.Snapshot);
        Assert.Equal(@"C:\Program Files\Vendor\App-1.1\App.exe", _database.Apps.Single().ExePath);
        Assert.Equal(@"C:\Program Files\Vendor\App-1.1\App.exe", result.Snapshot!.Apps.Single().ExePath);
        _appConfigService.Verify(service => service.SaveConfigForApp(
            app.Id,
            _database,
            It.IsAny<ISecureSecretSnapshotSource>(),
            It.IsAny<byte[]>()), Times.Once);
    }

    [Fact]
    public void Run_WhenDefaultRepairSaveFails_ShowsStartupRepairWarningAndSkipsBackgroundWork()
    {
        var app = new AppEntry
        {
            Id = "df01",
            Name = "DefaultRepairApp",
            ExePath = @"C:\Vendor\App\app.exe",
            IsFolder = true,
            AclTarget = AclTarget.File,
            AclMode = AclMode.Deny,
            AllowedAclEntries = [new AllowAclEntry { Sid = "S-1-5-21-1", AllowWrite = true }]
        };
        _database.Apps = [app];
        _sessionSaver.Setup(service => service.SaveConfig())
            .Throws(new InvalidOperationException("default save failed"));

        using var session = new SessionContext
        {
            Database = _database,
            CredentialStore = new CredentialStore()
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));
        var sessionProvider = new LambdaSessionProvider(() => session);
        var folderHandlerService = CreateFolderHandlerServiceMock();
        var packageInstallService = new Mock<IPackageInstallService>();
        var contextMenuService = new Mock<IContextMenuService>();
        var appHandlerRegistrationService = new Mock<IAppHandlerRegistrationService>();
        var handlerMappingService = new Mock<IHandlerMappingService>();

        var deferredRunner = new DeferredStartupRunner(
            folderHandlerService.Object,
            CreateFeatureActivator(sessionProvider, contextMenuService, appHandlerRegistrationService, handlerMappingService),
            CreateStartupRunner(
                sessionProvider,
                new TestBackupIntentFileSystem().WithExistingFile(app.ExePath)),
            sessionProvider,
            _sessionSaver.Object,
            packageInstallService.Object,
            _startupRepairWarningPresenter.Object,
            new StartupOptions(IsBackground: false, PinBypassed: false),
            _log.Object);

        string? warningMessage = null;
        _startupRepairWarningPresenter.Setup(
                presenter => presenter.ShowStartupRepairWarning(It.IsAny<string>()))
            .Callback<string>(message => warningMessage = message);

        deferredRunner.Run(new DeferredStartupMainFormProbe());

        Assert.NotNull(warningMessage);

        Assert.Contains("invalid application defaults", warningMessage!, StringComparison.Ordinal);
        folderHandlerService.Verify(service => service.CaptureCleanupSidSnapshot(), Times.Never);
        folderHandlerService.Verify(service => service.CleanupStaleRegistrations(It.IsAny<IReadOnlyCollection<string>>()), Times.Never);
        folderHandlerService.Verify(service => service.CleanupStaleRegistrations(), Times.Never);
        packageInstallService.Verify(service => service.CleanupStaleScripts(), Times.Never);
        contextMenuService.Verify(service => service.Register(), Times.Never);
        contextMenuService.Verify(service => service.Unregister(), Times.Never);
        handlerMappingService.Verify(service => service.GetEffectiveHandlerMappings(It.IsAny<AppDatabase>()), Times.Never);
        appHandlerRegistrationService.Verify(
            service => service.Sync(It.IsAny<Dictionary<string, HandlerMappingEntry>>(), It.IsAny<List<AppEntry>>()),
            Times.Never);
        _startupRepairWarningPresenter.Verify(
            presenter => presenter.ShowStartupRepairWarning(
                It.Is<string>(message => message.Contains("invalid application defaults", StringComparison.Ordinal))),
            Times.Once);
    }

    [Fact]
    public void Run_WhenPathRepairSaveFails_ShowsStartupRepairWarningAndSkipsBackgroundWork()
    {
        var app = new AppEntry
        {
            Id = "path01",
            Name = "PathRepairApp",
            ExePath = @"C:\Program Files\Vendor\App-1.0\App.exe",
            AclMode = AclMode.Allow
        };
        _database.Apps = [app];
        _appConfigService.Setup(service => service.SaveConfigForApp(
                app.Id,
                It.IsAny<AppDatabase>(),
                It.IsAny<ISecureSecretSnapshotSource>(),
                It.IsAny<byte[]>()))
            .Throws(new InvalidOperationException("path save failed"));

        using var session = new SessionContext
        {
            Database = _database,
            CredentialStore = new CredentialStore()
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));
        var sessionProvider = new LambdaSessionProvider(() => session);
        var folderHandlerService = CreateFolderHandlerServiceMock();
        var packageInstallService = new Mock<IPackageInstallService>();
        var contextMenuService = new Mock<IContextMenuService>();
        var appHandlerRegistrationService = new Mock<IAppHandlerRegistrationService>();
        var handlerMappingService = new Mock<IHandlerMappingService>();

        var deferredRunner = new DeferredStartupRunner(
            folderHandlerService.Object,
            CreateFeatureActivator(sessionProvider, contextMenuService, appHandlerRegistrationService, handlerMappingService),
            CreateStartupRunner(
                sessionProvider,
                new TestBackupIntentFileSystem()
                    .WithMissingFile(app.ExePath)
                    .WithExistingDirectory(@"C:\Program Files\Vendor")
                    .WithEnumeratedDirectories(@"C:\Program Files\Vendor", [@"C:\Program Files\Vendor\App-1.1"])
                    .WithExistingFile(@"C:\Program Files\Vendor\App-1.1\App.exe"),
                programFilesRoots: [@"C:\Program Files"]),
            sessionProvider,
            _sessionSaver.Object,
            packageInstallService.Object,
            _startupRepairWarningPresenter.Object,
            new StartupOptions(IsBackground: false, PinBypassed: false),
            _log.Object);

        string? warningMessage = null;
        _startupRepairWarningPresenter.Setup(
                presenter => presenter.ShowStartupRepairWarning(It.IsAny<string>()))
            .Callback<string>(message => warningMessage = message);

        deferredRunner.Run(new DeferredStartupMainFormProbe());

        Assert.NotNull(warningMessage);

        Assert.Contains("repaired missing application paths", warningMessage!, StringComparison.Ordinal);
        folderHandlerService.Verify(service => service.CaptureCleanupSidSnapshot(), Times.Never);
        folderHandlerService.Verify(service => service.CleanupStaleRegistrations(It.IsAny<IReadOnlyCollection<string>>()), Times.Never);
        folderHandlerService.Verify(service => service.CleanupStaleRegistrations(), Times.Never);
        packageInstallService.Verify(service => service.CleanupStaleScripts(), Times.Never);
        contextMenuService.Verify(service => service.Register(), Times.Never);
        contextMenuService.Verify(service => service.Unregister(), Times.Never);
        handlerMappingService.Verify(service => service.GetEffectiveHandlerMappings(It.IsAny<AppDatabase>()), Times.Never);
        appHandlerRegistrationService.Verify(
            service => service.Sync(It.IsAny<Dictionary<string, HandlerMappingEntry>>(), It.IsAny<List<AppEntry>>()),
            Times.Never);
        _startupRepairWarningPresenter.Verify(
            presenter => presenter.ShowStartupRepairWarning(
                It.Is<string>(message => message.Contains("missing application paths", StringComparison.Ordinal))),
            Times.Once);
    }

    [Fact]
    public async Task Run_WhenDeferredEnforcementThrows_LogsWarningAndNoMessageBoxActionsAreScheduled()
    {
        var warning = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _log.Setup(log => log.Warn(It.IsAny<string>()))
            .Callback<string>(message => warning.TrySetResult(message));

        var enforcementService = new Mock<IStartupEnforcementService>();
        enforcementService.Setup(service => service.Enforce(It.IsAny<AppDatabase>()))
            .Throws(new InvalidOperationException("startup enforcement failed"));

        using var session = new SessionContext
        {
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore()
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));
        var sessionProvider = new LambdaSessionProvider(() => session);
        var folderHandlerService = CreateFolderHandlerServiceMock();

        var startupRunner = CreateStartupRunner(
            sessionProvider,
            new TestBackupIntentFileSystem(),
            startupEnforcementService: enforcementService.Object);
        var deferredRunner = new DeferredStartupRunner(
            folderHandlerService.Object,
            CreateFeatureActivator(sessionProvider),
            startupRunner,
            sessionProvider,
            _sessionSaver.Object,
            new Mock<IPackageInstallService>().Object,
            _startupRepairWarningPresenter.Object,
            new StartupOptions(IsBackground: false, PinBypassed: false),
            _log.Object);

        var mainForm = new DeferredStartupMainFormProbe();
        deferredRunner.Run(mainForm);

        var warningMessage = await warning.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("Deferred startup enforcement failed", warningMessage);
        Assert.Empty(mainForm.ScheduledActions);
        Assert.Equal(0, mainForm.ShowTrayBalloonCallCount);
    }

    [Fact]
    public async Task Run_CapturesCleanupSidSnapshotOnUiThread_AndPassesItToBackgroundCleanup()
    {
        using var session = new SessionContext
        {
            Database = new AppDatabase(),
            CredentialStore = new CredentialStore()
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));
        var sessionProvider = new LambdaSessionProvider(() => session);
        var cleanupCalled = new TaskCompletionSource<IReadOnlyCollection<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var snapshot = (IReadOnlyList<string>)["S-1-5-21-100-200-300-400"];
        var folderHandlerService = CreateFolderHandlerServiceMock();
        folderHandlerService.Setup(service => service.CaptureCleanupSidSnapshot())
            .Returns(snapshot);
        folderHandlerService.Setup(service => service.CleanupStaleRegistrations(It.IsAny<IReadOnlyCollection<string>>()))
            .Callback<IReadOnlyCollection<string>>(captured => cleanupCalled.TrySetResult(captured));

        var deferredRunner = new DeferredStartupRunner(
            folderHandlerService.Object,
            CreateFeatureActivator(sessionProvider),
            CreateStartupRunner(sessionProvider, new TestBackupIntentFileSystem()),
            sessionProvider,
            _sessionSaver.Object,
            new Mock<IPackageInstallService>().Object,
            _startupRepairWarningPresenter.Object,
            new StartupOptions(IsBackground: false, PinBypassed: false),
            _log.Object);

        deferredRunner.Run(new DeferredStartupMainFormProbe());

        var cleanupSnapshot = await cleanupCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(snapshot, cleanupSnapshot);
        folderHandlerService.Verify(service => service.CaptureCleanupSidSnapshot(), Times.Once);
        folderHandlerService.Verify(service => service.CleanupStaleRegistrations(It.IsAny<IReadOnlyCollection<string>>()), Times.Once);
    }

    private StartupFeatureActivator CreateFeatureActivator(
        ISessionProvider sessionProvider,
        Mock<IContextMenuService>? contextMenuService = null,
        Mock<IAppHandlerRegistrationService>? appHandlerRegistrationService = null,
        Mock<IHandlerMappingService>? handlerMappingService = null)
    {
        var resolvedContextMenuService = contextMenuService ?? new Mock<IContextMenuService>();
        var resolvedAppHandlerRegistrationService = appHandlerRegistrationService ?? new Mock<IAppHandlerRegistrationService>();
        var resolvedHandlerMappingService = handlerMappingService ?? new Mock<IHandlerMappingService>();
        resolvedHandlerMappingService.Setup(service => service.GetEffectiveHandlerMappings(It.IsAny<AppDatabase>()))
            .Returns(new Dictionary<string, HandlerMappingEntry>());

        return new StartupFeatureActivator(
            resolvedContextMenuService.Object,
            resolvedAppHandlerRegistrationService.Object,
            resolvedHandlerMappingService.Object,
            new WizardLauncher(() => throw new InvalidOperationException("Wizard should not open in this test.")),
            sessionProvider,
            new StartupOptions(IsBackground: false, PinBypassed: false),
            null!,
            new Mock<IStartupUnlockGrant>().Object,
            new Mock<IEvaluationCredentialCounter>().Object);
    }

    private static Mock<IFolderHandlerService> CreateFolderHandlerServiceMock()
    {
        var mock = new Mock<IFolderHandlerService>();
        mock.Setup(service => service.CaptureCleanupSidSnapshot()).Returns([]);
        return mock;
    }

    private StartupEnforcementRunner CreateStartupRunner(
        ISessionProvider sessionProvider,
        IBackupIntentFileSystem fileSystem,
        IStartupEnforcementService? startupEnforcementService = null,
        IReadOnlyList<string>? programFilesRoots = null,
        IReadOnlyDictionary<string, string>? profilePaths = null)
    {
        if (startupEnforcementService is null)
        {
            var startupEnforcementServiceMock = new Mock<IStartupEnforcementService>();
            startupEnforcementServiceMock.Setup(service => service.Enforce(It.IsAny<AppDatabase>()))
                .Returns(new EnforcementResult(new Dictionary<string, DateTime>(), new List<ContainerTraverseGrant>()));
            startupEnforcementService = startupEnforcementServiceMock.Object;
        }

        var appContainerService = new Mock<IAppContainerService>();
        var pathGrantService = new Mock<IGrantMutatorService>();
        var interactiveUserResolver = new Mock<IInteractiveUserResolver>();
        var aclPermission = new Mock<IAclPermissionService>();
        aclPermission.Setup(service => service.HasEffectiveRights(
                It.IsAny<System.Security.AccessControl.FileSystemSecurity>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<System.Security.AccessControl.FileSystemRights>()))
            .Returns(true);

        var sidReconciler = new SidReconciler(
            aclPermission.Object,
            () => new AncestorTraverseGranter(
                _log.Object,
                aclPermission.Object,
                new Mock<ITraverseAcl>().Object,
                new TestFileSystemPathInfo()),
            _log.Object,
            new Mock<IInteractiveUserResolver>().Object,
            new TestFileSystemPathInfo(),
            new Mock<IProgramDataDirectoryProvisioningService>().Object,
            Mock.Of<IProgramDataKnownPathResolver>());
        var reconciliationService = new GrantReconciliationService(
            aclPermission.Object,
            new Mock<ILocalGroupQueryService>().Object,
            _log.Object,
            _sessionSaver.Object,
            new LambdaDatabaseProvider(() => _database),
            sidReconciler);
        var persistenceHelper = new SessionPersistenceHelper(
            new Mock<IConfigReencryptionPersistence>().Object,
            new Mock<IMainConfigPersistence>().Object,
            new Mock<ISidNameCacheService>().Object,
            () => new InlineUiThreadInvoker(action => action()),
            _log.Object);
        var ephemeralAccountService = new EphemeralAccountService(
            new Mock<IAccountDeletionService>().Object,
            persistenceHelper,
            new Mock<ILocalUserProvider>().Object,
            _log.Object,
            new Mock<IAccountValidationService>().Object,
            sessionProvider,
            new Mock<IUiThreadInvoker>().Object,
            new Mock<ITrayBalloonService>().Object,
            new Mock<ISidResolver>().Object,
            new Mock<IGrantAccountCleanupService>().Object,
            new Mock<ITrackingJobStateStore>().Object);
        var ephemeralContainerService = new EphemeralContainerService(
            new Mock<IContainerDeletionService>().Object,
            new Mock<IDatabaseService>().Object,
            _log.Object,
            sessionProvider,
            new Mock<IUiThreadInvoker>().Object,
            new Mock<IProcessListService>().Object,
            new Mock<ITrayBalloonService>().Object);

        return new StartupEnforcementRunner(
            startupEnforcementService,
            sessionProvider,
            _sessionSaver.Object,
            pathGrantService.Object,
            ephemeralAccountService,
            ephemeralContainerService,
            reconciliationService,
            appContainerService.Object,
            interactiveUserResolver.Object,
            _log.Object,
            new EnforcementResultApplier(appContainerService.Object, new TraverseGrantOwnerResolver()),
            CreateRepairCoordinator(sessionProvider, fileSystem, programFilesRoots, profilePaths));
    }

    private AppEntryPathRepairCoordinator CreateRepairCoordinator(
        ISessionProvider sessionProvider,
        IBackupIntentFileSystem fileSystem,
        IReadOnlyList<string>? programFilesRoots,
        IReadOnlyDictionary<string, string>? profilePaths)
    {
        var programFilesProvider = new Mock<IProgramFilesPathProvider>();
        programFilesProvider.Setup(provider => provider.GetProgramFilesRoots())
            .Returns(programFilesRoots ?? []);

        var profilePathResolver = new Mock<IProfilePathResolver>();
        profilePathResolver.Setup(resolver => resolver.TryGetProfilePath(It.IsAny<string>()))
            .Returns<string>(sid =>
                profilePaths != null && profilePaths.TryGetValue(sid, out var path) ? path : null);

        var enforcementCoordinator = AppEntryEnforcementTestFactory.CreateCoordinator(
            _aclService.Object,
            _shortcutService.Object,
            _besideTargetShortcutService.Object,
            _iconService.Object,
            _sidNameCache.Object,
            _desktopProvider.Object,
            _interactiveUserSidResolver.Object,
            new TestRunFenceLauncherPathProvider(@"C:\RunFence\RunFence.Launcher.exe", exists: true),
            _log.Object);

        return new AppEntryPathRepairCoordinator(
            new VersionedPathAutoRepairTrustPolicy(programFilesProvider.Object, profilePathResolver.Object),
            new VersionedPathRepairer(fileSystem),
            new VersionedPathRepairOptionsBuilder(profilePathResolver.Object),
            new AppEntryPathRepairCommitter(
                sessionProvider,
                _appConfigService.Object,
                _iconService.Object,
                enforcementCoordinator,
                _aclService.Object));
    }

    private sealed class DeferredStartupMainFormProbe : IDeferredStartupMainForm
    {
        private readonly List<Action> _scheduledActions = [];

        public bool IsDisposed => false;
        public IReadOnlyList<Action> ScheduledActions => _scheduledActions;
        public int ShowTrayBalloonCallCount { get; private set; }

        public void BeginInvokeOnUiThread(Action action) => _scheduledActions.Add(action);
        public void ShowTrayBalloon(string text) => ShowTrayBalloonCallCount++;
    }

    private sealed class TestBackupIntentFileSystem : IBackupIntentFileSystem
    {
        private readonly Dictionary<string, BackupIntentPathState> _fileStates = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, BackupIntentPathState> _directoryStates = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IReadOnlyList<string>> _enumerations = new(StringComparer.OrdinalIgnoreCase);

        public TestBackupIntentFileSystem WithExistingFile(string path)
        {
            _fileStates[Normalize(path)] = BackupIntentPathState.Exists;
            return this;
        }

        public TestBackupIntentFileSystem WithMissingFile(string path)
        {
            _fileStates[Normalize(path)] = BackupIntentPathState.Missing;
            return this;
        }

        public TestBackupIntentFileSystem WithExistingDirectory(string path)
        {
            _directoryStates[Normalize(path)] = BackupIntentPathState.Exists;
            return this;
        }

        public TestBackupIntentFileSystem WithEnumeratedDirectories(string path, IReadOnlyList<string> directories)
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
