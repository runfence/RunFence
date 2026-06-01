using Moq;
using RunFence.Acl;
using RunFence.Account;
using RunFence.Apps;
using RunFence.Apps.UI;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Persistence;
using RunFence.RunAs;
using Xunit;

namespace RunFence.Tests;

public sealed class ApplicationsPanelLaunchHandlerTests : IDisposable
{
    private const string TestSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
    private readonly SecureSecret _protectedPinKey;
    private readonly AppDatabase _database;
    private readonly Mock<IAppConfigService> _appConfigService = new();
    private readonly Mock<IAclService> _aclService = new();
    private readonly Mock<IShortcutService> _shortcutService = new();
    private readonly Mock<IBesideTargetShortcutService> _besideTargetShortcutService = new();
    private readonly Mock<ISidNameCacheService> _sidNameCache = new();
    private readonly Mock<ILaunchFeedbackPresenter> _launchFeedbackPresenter = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<IMessageBoxService> _messageBoxService = new();
    private readonly Mock<IRunAsFlowHandler> _runAsFlowHandler = new();

    public ApplicationsPanelLaunchHandlerTests()
    {
        _protectedPinKey = TestSecretFactory.FromBytes(new byte[32]);
        _database = new AppDatabase
        {
            SidNames = { [TestSid] = "TestUser" }
        };
    }

    public void Dispose()
    {
        _protectedPinKey.Dispose();
    }

    [Fact]
    public void LaunchApp_UsesInjectedLauncher()
    {
        var app = new AppEntry
        {
            Id = "direct01",
            AccountSid = TestSid,
            Name = "Direct App",
            ExePath = @"C:\Tools\App.exe"
        };
        var injectedLauncher = new Mock<IAppEntryLauncher>(MockBehavior.Strict);
        AppEntry? launchedApp = null;
        injectedLauncher
            .Setup(launcher => launcher.Launch(
                It.IsAny<AppEntry>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<Func<string, string, bool>?>(),
                It.IsAny<string?>()))
            .Callback<AppEntry, string?, string?, Func<string, string, bool>?, string?>((appArg, _, _, _, _) => launchedApp = appArg)
            .Returns(() => new LaunchExecutionResult(
                LaunchExecutionStatus.ProcessStarted,
                null,
                []))
            .Verifiable();

        var handler = BuildHandler(injectedLauncher.Object);
        _sidNameCache.Setup(service => service.GetDisplayName(It.IsAny<string>())).Returns("TestUser");

        handler.LaunchApp(app, null, null);

        Assert.Same(app, launchedApp);
        injectedLauncher.Verify(
            launcher => launcher.Launch(
                app,
                null,
                null,
                It.Is<Func<string, string, bool>?>(prompt => prompt != null),
                null),
            Times.Once);
        _launchFeedbackPresenter.Verify(
            presenter => presenter.ShowMaintenanceWarning(
                It.IsAny<LaunchExecutionResult>(),
                It.Is<LaunchFeedbackContext>(context =>
                    context.Source == LaunchFeedbackSource.InteractiveUi &&
                    context.SummaryName == app.Name &&
                    context.Owner == null)),
            Times.Once);
    }

    [Fact]
    public void LaunchApp_UsesRepairingLauncherToRepairTrustedVersionedPath()
    {
        const string originalPath = @"C:\Program Files\Vendor\App-1.0\App.exe";
        const string repairedPath = @"C:\Program Files\Vendor\App-1.1\App.exe";

        var app = new AppEntry
        {
            Id = "repair01",
            AccountSid = TestSid,
            Name = "Repair App",
            ExePath = originalPath,
        };
        _database.Apps.Add(app);

        var sessionProvider = new Mock<ISessionProvider>();
        sessionProvider.Setup(service => service.GetSession())
            .Returns(new SessionContext
            {
                Database = _database,
                CredentialStore = new CredentialStore(),
            }.WithClonedPinDerivedKey(_protectedPinKey));

        var launchExecutor = new Mock<IAppEntryLaunchExecutor>(MockBehavior.Strict);
        AppEntry? launchedApp = null;
        launchExecutor
            .Setup(executor => executor.Launch(
                It.IsAny<AppEntry>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<Func<string, string, bool>?>(),
                It.IsAny<string?>()))
            .Callback<AppEntry, string?, string?, Func<string, string, bool>?, string?>((appArg, _, _, _, _) => launchedApp = appArg)
            .Returns(new LaunchExecutionResult(LaunchExecutionStatus.ProcessStarted, null, []))
            .Verifiable();

        var handler = BuildHandler(BuildRepairingLauncher(
            sessionProvider,
            launchExecutor,
            new TestBackupIntentFileSystem()
                .WithMissingFile(originalPath)
                .WithExistingDirectory(@"C:\Program Files\Vendor")
                .WithEnumeratedDirectories(@"C:\Program Files\Vendor", [@"C:\Program Files\Vendor\App-1.1"])
                .WithExistingFile(repairedPath)));

        _sidNameCache.Setup(service => service.GetDisplayName(It.IsAny<string>())).Returns("TestUser");

        handler.LaunchApp(app, null, null);

        Assert.NotNull(launchedApp);
        Assert.Equal(repairedPath, launchedApp!.ExePath);
        launchExecutor.Verify(
            executor => executor.Launch(
                It.Is<AppEntry>(candidate => candidate.ExePath == repairedPath),
                null,
                null,
                It.Is<Func<string, string, bool>?>(prompt => prompt != null),
                null),
            Times.Once);
    }

    private ApplicationsPanelLaunchHandler BuildHandler(IAppEntryLauncher launcher)
        => new(
            launcher,
            _sidNameCache.Object,
            _launchFeedbackPresenter.Object,
            _log.Object,
            _messageBoxService.Object,
            _runAsFlowHandler.Object);

    private RepairingAppEntryLauncher BuildRepairingLauncher(
        Mock<ISessionProvider> sessionProvider,
        Mock<IAppEntryLaunchExecutor> launchExecutor,
        IBackupIntentFileSystem fileSystem)
    {
        var fileSystemProvider = new Mock<IProgramFilesPathProvider>();
        fileSystemProvider.Setup(provider => provider.GetProgramFilesRoots()).Returns([@"C:\Program Files"]);
        var profilePathResolver = new Mock<IProfilePathResolver>();
        var uiService = BuildRepairService(sessionProvider, fileSystemProvider.Object, profilePathResolver.Object, fileSystem);

        return new RepairingAppEntryLauncher(sessionProvider.Object, uiService, launchExecutor.Object);
    }

    private UiThreadAppEntryPathRepairService BuildRepairService(
        Mock<ISessionProvider> sessionProvider,
        IProgramFilesPathProvider programFilesProvider,
        IProfilePathResolver profilePathResolver,
        IBackupIntentFileSystem fileSystem)
    {
        var coordinator = new AppEntryPathRepairCoordinator(
            new VersionedPathAutoRepairTrustPolicy(programFilesProvider, profilePathResolver),
            new VersionedPathRepairer(fileSystem),
            new VersionedPathRepairOptionsBuilder(profilePathResolver),
            new AppEntryPathRepairCommitter(
                sessionProvider.Object,
                _appConfigService.Object,
                Mock.Of<IIconService>(),
                AppEntryEnforcementTestFactory.CreateCoordinator(
                    _aclService.Object,
                    _shortcutService.Object,
                    _besideTargetShortcutService.Object,
                    Mock.Of<IIconService>(),
                    _sidNameCache.Object,
                    Mock.Of<IInteractiveUserDesktopProvider>(),
                    Mock.Of<IInteractiveUserSidResolver>(),
                    new TestRunFenceLauncherPathProvider(@"C:\RunFence\RunFence.Launcher.exe", exists: true),
                    _log.Object),
                _aclService.Object));
        var accessor = new UiThreadDatabaseAccessor(
            new LambdaDatabaseProvider(() => _database),
            () => new InlineUiThreadInvoker(action => action()));
        return new UiThreadAppEntryPathRepairService(accessor, coordinator);
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
