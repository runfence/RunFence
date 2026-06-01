using Moq;
using RunFence.Acl;
using RunFence.Acl.Permissions;
using RunFence.Acl.Traverse;
using RunFence.Account;
using RunFence.Account.Lifecycle;
using RunFence.Apps;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Launch.Container;
using RunFence.Persistence;
using RunFence.Persistence.UI;
using RunFence.Startup;
using RunFence.Startup.UI;
using RunFence.UI;
using Xunit;

namespace RunFence.Tests;

public sealed class MainFormStartupOrchestratorTests
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
    private readonly Mock<IStartupEnforcementMessagePresenter> _startupEnforcementMessagePresenter = new();

    public MainFormStartupOrchestratorTests()
    {
        _iconService.Setup(service => service.CreateBadgedIcon(It.IsAny<AppEntry>())).Returns("icon.ico");
        _sidNameCache.Setup(cache => cache.GetDisplayName(It.IsAny<string>())).Returns<string>(sid => sid);
    }

    [Fact]
    public async Task RunStartupChecksAsync_VisibleStartup_DoesNotReactivateOwnerAfterChecks()
    {
        var startupSecurityService = new Mock<IStartupSecurityService>(MockBehavior.Strict);
        startupSecurityService.Setup(service => service.RunChecks(It.IsAny<CancellationToken>()))
            .Returns([]);

        var session = new SessionContext
        {
            Database = _database,
            CredentialStore = new CredentialStore()
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));

        var firstRunExporter = new Mock<IMainFormFirstRunExporter>(MockBehavior.Strict);
        firstRunExporter.Setup(exporter => exporter.PromptExportSettingsIfNeededAsync(It.IsAny<IWin32Window>()))
            .Returns(Task.CompletedTask);

        var orchestrator = CreateOrchestrator(
            session,
            CreateStartupRunner(new LambdaSessionProvider(() => session), new TestBackupIntentFileSystem()),
            startupSecurityService: startupSecurityService.Object,
            firstRunExporter: firstRunExporter.Object);

        using var owner = new Form();
        var nagShown = false;

        await orchestrator.RunStartupChecksAsync(owner, suppressVisibility: false, () => nagShown = true);

        Assert.True(nagShown);
        Assert.False(owner.IsHandleCreated);
        firstRunExporter.Verify(exporter => exporter.PromptExportSettingsIfNeededAsync(owner), Times.Once);
    }

    [Fact]
    public void PrepareEnforcementSnapshot_RepairSuccess_UsesRepairedSnapshot()
    {
        var app = new AppEntry
        {
            Id = "pf020",
            ExePath = @"C:\Program Files\Vendor\App-1.0\App.exe"
        };
        _database.Apps = [app];

        var session = new SessionContext
        {
            Database = _database,
            CredentialStore = new CredentialStore()
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));

        var orchestrator = CreateOrchestrator(
            session,
            CreateStartupRunner(
                new LambdaSessionProvider(() => session),
                new TestBackupIntentFileSystem()
                    .WithMissingFile(app.ExePath)
                    .WithExistingDirectory(@"C:\Program Files\Vendor")
                    .WithEnumeratedDirectories(@"C:\Program Files\Vendor", [@"C:\Program Files\Vendor\App-1.1"])
                    .WithExistingFile(@"C:\Program Files\Vendor\App-1.1\App.exe"),
                programFilesRoots: [@"C:\Program Files"]));

        var result = orchestrator.PrepareEnforcementSnapshot();

        Assert.Null(result.SaveFailureMessage);
        Assert.NotNull(result.Snapshot);
        Assert.Equal(@"C:\Program Files\Vendor\App-1.1\App.exe", result.Snapshot!.Apps.Single().ExePath);
    }

    [Fact]
    public void PrepareEnforcementSnapshot_RepairSaveFailure_ReturnsFailureAndSkipsSnapshot()
    {
        var app = new AppEntry
        {
            Id = "pf021",
            Name = "RepairApp",
            ExePath = @"C:\Program Files\Vendor\App-1.0\App.exe"
        };
        _database.Apps = [app];
        _appConfigService.Setup(service => service.SaveConfigForApp(
                It.IsAny<string>(),
                It.IsAny<AppDatabase>(),
                It.IsAny<ISecureSecretSnapshotSource>(),
                It.IsAny<byte[]>()))
            .Throws(new InvalidOperationException("save failed"));

        var session = new SessionContext
        {
            Database = _database,
            CredentialStore = new CredentialStore()
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));

        var orchestrator = CreateOrchestrator(
            session,
            CreateStartupRunner(
                new LambdaSessionProvider(() => session),
                new TestBackupIntentFileSystem()
                    .WithMissingFile(app.ExePath)
                    .WithExistingDirectory(@"C:\Program Files\Vendor")
                    .WithEnumeratedDirectories(@"C:\Program Files\Vendor", [@"C:\Program Files\Vendor\App-1.1"])
                    .WithExistingFile(@"C:\Program Files\Vendor\App-1.1\App.exe"),
                programFilesRoots: [@"C:\Program Files"]));

        var result = orchestrator.PrepareEnforcementSnapshot();

        Assert.NotNull(result.SaveFailureMessage);
        Assert.Contains("RepairApp", result.SaveFailureMessage, StringComparison.Ordinal);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public void PrepareEnforcementSnapshot_UntrustedPath_SkipsRepairProbeAndUsesOriginalPath()
    {
        var fileSystem = new CountingBackupIntentFileSystem();
        var app = new AppEntry
        {
            Id = "drv20",
            ExePath = @"D:\Apps\Vendor\App-1.0\App.exe"
        };
        _database.Apps = [app];

        var session = new SessionContext
        {
            Database = _database,
            CredentialStore = new CredentialStore()
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));

        var orchestrator = CreateOrchestrator(
            session,
            CreateStartupRunner(
                new LambdaSessionProvider(() => session),
                fileSystem,
                programFilesRoots: [@"C:\Program Files"]));

        var result = orchestrator.PrepareEnforcementSnapshot();

        Assert.Null(result.SaveFailureMessage);
        Assert.NotNull(result.Snapshot);
        Assert.Equal(app.ExePath, result.Snapshot!.Apps.Single().ExePath);
        Assert.Equal(0, fileSystem.FileStateCallCount);
        Assert.Equal(0, fileSystem.DirectoryStateCallCount);
        _appConfigService.Verify(service => service.SaveConfigForApp(
            It.IsAny<string>(),
            It.IsAny<AppDatabase>(),
            It.IsAny<ISecureSecretSnapshotSource>(),
            It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public void PrepareEnforcementSnapshot_TrustedPathWithoutCandidate_UsesOriginalPath()
    {
        var app = new AppEntry
        {
            Id = "pf022",
            ExePath = @"C:\Program Files\Vendor\App-1.0\App.exe"
        };
        _database.Apps = [app];

        var session = new SessionContext
        {
            Database = _database,
            CredentialStore = new CredentialStore()
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));

        var orchestrator = CreateOrchestrator(
            session,
            CreateStartupRunner(
                new LambdaSessionProvider(() => session),
                new CountingBackupIntentFileSystem()
                    .WithMissingFile(app.ExePath)
                    .WithExistingDirectory(@"C:\Program Files\Vendor")
                    .WithEnumeratedDirectories(@"C:\Program Files\Vendor", [@"C:\Program Files\Vendor\App-1.1"]),
                programFilesRoots: [@"C:\Program Files"]));

        var result = orchestrator.PrepareEnforcementSnapshot();

        Assert.Null(result.SaveFailureMessage);
        Assert.NotNull(result.Snapshot);
        Assert.Equal(app.ExePath, result.Snapshot!.Apps.Single().ExePath);
        _appConfigService.Verify(service => service.SaveConfigForApp(
            It.IsAny<string>(),
            It.IsAny<AppDatabase>(),
            It.IsAny<ISecureSecretSnapshotSource>(),
            It.IsAny<byte[]>()), Times.Never);
        _aclService.Verify(service => service.ApplyAcl(It.IsAny<AppEntry>(), It.IsAny<IReadOnlyList<AppEntry>>()), Times.Never);
    }

    [Fact]
    public void RunEnforcement_RepairSaveFailure_ReportsFailureAndSkipsEnforcement()
    {
        var app = new AppEntry
        {
            Id = "pf023",
            Name = "RepairApp",
            ExePath = @"C:\Program Files\Vendor\App-1.0\App.exe"
        };
        _database.Apps = [app];
        _appConfigService.Setup(service => service.SaveConfigForApp(
                It.IsAny<string>(),
                It.IsAny<AppDatabase>(),
                It.IsAny<ISecureSecretSnapshotSource>(),
                It.IsAny<byte[]>()))
            .Throws(new InvalidOperationException("save failed"));

        var enforcementService = new Mock<IStartupEnforcementService>(MockBehavior.Strict);
        var session = new SessionContext
        {
            Database = _database,
            CredentialStore = new CredentialStore()
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));

        var orchestrator = CreateOrchestrator(
            session,
            CreateStartupRunner(
                new LambdaSessionProvider(() => session),
                new TestBackupIntentFileSystem()
                    .WithMissingFile(app.ExePath)
                    .WithExistingDirectory(@"C:\Program Files\Vendor")
                    .WithEnumeratedDirectories(@"C:\Program Files\Vendor", [@"C:\Program Files\Vendor\App-1.1"])
                    .WithExistingFile(@"C:\Program Files\Vendor\App-1.1\App.exe"),
                programFilesRoots: [@"C:\Program Files"]),
            enforcementService.Object);

        _startupEnforcementMessagePresenter.Reset();
        _startupEnforcementMessagePresenter.Setup(
            service => service.ShowRepairSaveFailure(
                It.Is<string>(message => message.Contains("RepairApp", StringComparison.Ordinal))));

        using var owner = new Form();
        using var guardOwner = new Control();
        orchestrator.RunEnforcement(owner, guardOwner);

        enforcementService.Verify(service => service.Enforce(It.IsAny<AppDatabase>()), Times.Never);
        _startupEnforcementMessagePresenter.Verify(
            service => service.ShowRepairSaveFailure(
                It.Is<string>(message => message.Contains("RepairApp", StringComparison.Ordinal))),
            Times.Once);
        _startupEnforcementMessagePresenter.Verify(service => service.ShowSuccess(), Times.Never);
        _startupEnforcementMessagePresenter.Verify(service => service.ShowShortcutWarning(It.IsAny<string>()), Times.Never);
        _startupEnforcementMessagePresenter.Verify(service => service.ShowEnforcementFailure(It.IsAny<Exception>()), Times.Never);
    }

    [Fact]
    public async Task RunEnforcement_WhenSuccessful_ShowsSuccessMessage()
    {
        var app = new AppEntry
        {
            Id = "pf024",
            ExePath = @"D:\Vendor\App.exe"
        };
        _database.Apps = [app];

        var presentationCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var enforcementService = new Mock<IStartupEnforcementService>();
        enforcementService.Setup(service => service.Enforce(It.IsAny<AppDatabase>())).Returns(() =>
        {
            return new EnforcementResult(new(), []);
        });

        var session = new SessionContext
        {
            Database = _database,
            CredentialStore = new CredentialStore()
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));

        var orchestrator = CreateOrchestrator(
            session,
            CreateStartupRunner(new LambdaSessionProvider(() => session), new TestBackupIntentFileSystem()),
            enforcementService.Object);

        _startupEnforcementMessagePresenter.Reset();
        _startupEnforcementMessagePresenter.Setup(service => service.ShowSuccess())
            .Callback(() => presentationCompleted.TrySetResult());

        using var owner = new Form();
        using var guardOwner = new Control();
        orchestrator.RunEnforcement(owner, guardOwner);

        await presentationCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        enforcementService.Verify(service => service.Enforce(It.IsAny<AppDatabase>()), Times.Once);
        _startupEnforcementMessagePresenter.Verify(service => service.ShowSuccess(), Times.Once);
        _startupEnforcementMessagePresenter.Verify(service => service.ShowShortcutWarning(It.IsAny<string>()), Times.Never);
        _startupEnforcementMessagePresenter.Verify(service => service.ShowRepairSaveFailure(It.IsAny<string>()), Times.Never);
        _startupEnforcementMessagePresenter.Verify(service => service.ShowEnforcementFailure(It.IsAny<Exception>()), Times.Never);
    }

    [Fact]
    public async Task RunEnforcement_WithShortcutWarnings_ShowsShortcutWarningMessage()
    {
        var app = new AppEntry
        {
            Id = "pf025",
            ExePath = @"D:\Vendor\App.exe"
        };
        _database.Apps = [app];
        var warnings = new[] { "shortcut one", "shortcut two" };
        var expectedWarningMessage = string.Join("\n\n", warnings);

        var presentationCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var enforcementService = new Mock<IStartupEnforcementService>();
        enforcementService.Setup(service => service.Enforce(It.IsAny<AppDatabase>())).Returns(() =>
        {
            return new EnforcementResult(new(), [], warnings.ToList());
        });

        var session = new SessionContext
        {
            Database = _database,
            CredentialStore = new CredentialStore()
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));

        var orchestrator = CreateOrchestrator(
            session,
            CreateStartupRunner(new LambdaSessionProvider(() => session), new TestBackupIntentFileSystem()),
            enforcementService.Object);

        _startupEnforcementMessagePresenter.Reset();
        _startupEnforcementMessagePresenter.Setup(service => service.ShowShortcutWarning(expectedWarningMessage))
            .Callback(() => presentationCompleted.TrySetResult());

        using var owner = new Form();
        using var guardOwner = new Control();
        orchestrator.RunEnforcement(owner, guardOwner);

        await presentationCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        enforcementService.Verify(service => service.Enforce(It.IsAny<AppDatabase>()), Times.Once);
        _startupEnforcementMessagePresenter.Verify(
            service => service.ShowShortcutWarning(expectedWarningMessage),
            Times.Once);
        _startupEnforcementMessagePresenter.Verify(service => service.ShowSuccess(), Times.Never);
        _startupEnforcementMessagePresenter.Verify(service => service.ShowRepairSaveFailure(It.IsAny<string>()), Times.Never);
        _startupEnforcementMessagePresenter.Verify(service => service.ShowEnforcementFailure(It.IsAny<Exception>()), Times.Never);
    }

    [Fact]
    public async Task RunEnforcement_EnforcementFailure_ShowsFailureMessage()
    {
        var app = new AppEntry
        {
            Id = "pf026",
            ExePath = @"D:\Vendor\App.exe"
        };
        _database.Apps = [app];

        var presentationCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var enforcementService = new Mock<IStartupEnforcementService>();
        enforcementService.Setup(service => service.Enforce(It.IsAny<AppDatabase>()))
            .Throws(new InvalidOperationException("boom"));

        var session = new SessionContext
        {
            Database = _database,
            CredentialStore = new CredentialStore()
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));

        var orchestrator = CreateOrchestrator(
            session,
            CreateStartupRunner(new LambdaSessionProvider(() => session), new TestBackupIntentFileSystem()),
            enforcementService.Object);

        _startupEnforcementMessagePresenter.Reset();
        _startupEnforcementMessagePresenter.Setup(service => service.ShowEnforcementFailure(
                It.Is<InvalidOperationException>(ex => ex.Message == "boom")))
            .Callback<Exception>(_ => presentationCompleted.TrySetResult());

        using var owner = new Form();
        using var guardOwner = new Control();
        orchestrator.RunEnforcement(owner, guardOwner);

        await presentationCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        enforcementService.Verify(service => service.Enforce(It.IsAny<AppDatabase>()), Times.Once);
        _startupEnforcementMessagePresenter.Verify(
            service => service.ShowEnforcementFailure(It.Is<InvalidOperationException>(ex => ex.Message == "boom")),
            Times.Once);
        _startupEnforcementMessagePresenter.Verify(service => service.ShowSuccess(), Times.Never);
        _startupEnforcementMessagePresenter.Verify(service => service.ShowRepairSaveFailure(It.IsAny<string>()), Times.Never);
        _startupEnforcementMessagePresenter.Verify(service => service.ShowShortcutWarning(It.IsAny<string>()), Times.Never);
    }

    private MainFormStartupOrchestrator CreateOrchestrator(
        SessionContext session,
        StartupEnforcementRunner startupEnforcementRunner,
        IStartupEnforcementService? startupEnforcementService = null,
        IStartupEnforcementMessagePresenter? startupEnforcementMessagePresenter = null,
        IStartupSecurityService? startupSecurityService = null,
        IMainFormFirstRunExporter? firstRunExporter = null)
    {
        var configSaveOrchestrator = new ConfigSaveOrchestrator(
            new LambdaSessionProvider(() => session),
            () => new InlineUiThreadInvoker(action => action()),
            new Mock<IDatabaseService>().Object,
            _appConfigService.Object,
            new Mock<IHandlerMappingService>().Object);
        if (firstRunExporter == null)
        {
            var firstRunExporterMock = new Mock<IMainFormFirstRunExporter>();
            firstRunExporterMock.Setup(exporter => exporter.PromptExportSettingsIfNeededAsync(It.IsAny<IWin32Window>()))
                .Returns(Task.CompletedTask);
            firstRunExporter = firstRunExporterMock.Object;
        }

        return new MainFormStartupOrchestrator(
            startupSecurityService ?? new Mock<IStartupSecurityService>().Object,
            _log.Object,
            configSaveOrchestrator,
            startupEnforcementService ?? new Mock<IStartupEnforcementService>().Object,
            startupEnforcementRunner,
            new ApplicationState(
                new LambdaSessionProvider(() => session),
                new Mock<ILockManager>().Object,
                new Mock<IModalTracker>().Object,
                new UiThreadDatabaseAccessor(
                    new LambdaDatabaseProvider(() => session.Database),
                    () => new InlineUiThreadInvoker(action => action()))),
            session,
            new EnforcementResultApplier(new Mock<IAppContainerService>().Object, new TraverseGrantOwnerResolver()),
            null!,
            firstRunExporter,
            startupEnforcementMessagePresenter ?? _startupEnforcementMessagePresenter.Object);
    }

    private StartupEnforcementRunner CreateStartupRunner(
        ISessionProvider sessionProvider,
        IBackupIntentFileSystem fileSystem,
        IReadOnlyList<string>? programFilesRoots = null,
        IReadOnlyDictionary<string, string>? profilePaths = null)
    {
        var enforcementService = new Mock<IStartupEnforcementService>();
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

        var pathInfo = new TestFileSystemPathInfo();
        var sidReconciler = new SidReconciler(
            aclPermission.Object,
            () => new AncestorTraverseGranter(
                _log.Object,
                aclPermission.Object,
                new Mock<ITraverseAcl>().Object,
                pathInfo),
            _log.Object,
            new Mock<IInteractiveUserResolver>().Object,
            pathInfo,
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
            enforcementService.Object,
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

    private sealed class CountingBackupIntentFileSystem : IBackupIntentFileSystem
    {
        private readonly Dictionary<string, BackupIntentPathState> _fileStates = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, BackupIntentPathState> _directoryStates = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IReadOnlyList<string>> _enumerations = new(StringComparer.OrdinalIgnoreCase);

        public int FileStateCallCount { get; private set; }

        public int DirectoryStateCallCount { get; private set; }

        public CountingBackupIntentFileSystem WithMissingFile(string path)
        {
            _fileStates[Normalize(path)] = BackupIntentPathState.Missing;
            return this;
        }

        public CountingBackupIntentFileSystem WithExistingDirectory(string path)
        {
            _directoryStates[Normalize(path)] = BackupIntentPathState.Exists;
            return this;
        }

        public CountingBackupIntentFileSystem WithEnumeratedDirectories(string path, IReadOnlyList<string> directories)
        {
            _enumerations[Normalize(path)] = directories.Select(Normalize).ToArray();
            return this;
        }

        public BackupIntentPathState GetFileState(string path)
        {
            FileStateCallCount++;
            return _fileStates.GetValueOrDefault(Normalize(path), BackupIntentPathState.Missing);
        }

        public BackupIntentPathState GetDirectoryState(string path)
        {
            DirectoryStateCallCount++;
            return _directoryStates.GetValueOrDefault(Normalize(path), BackupIntentPathState.Missing);
        }

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
