using Moq;
using RunFence.Acl;
using RunFence.Account;
using RunFence.Apps;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public sealed class RepairingAppEntryLauncherTests : IDisposable
{
    private readonly AppDatabase _database;
    private readonly Mock<ISessionProvider> _sessionProvider;
    private readonly Mock<IAppConfigService> _appConfigService = new();
    private readonly Mock<IAclService> _aclService = new();
    private readonly Mock<IShortcutService> _shortcutService = new();
    private readonly Mock<IBesideTargetShortcutService> _besideTargetShortcutService = new();
    private readonly Mock<IIconService> _iconService = new();
    private readonly Mock<ISidNameCacheService> _sidNameCache = new();
    private readonly Mock<IInteractiveUserDesktopProvider> _desktopProvider = new();
    private readonly Mock<IInteractiveUserSidResolver> _interactiveUserSidResolver = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<IAppEntryLaunchExecutor> _launchExecutor = new();
    private readonly byte[] _pinDerivedKey = new byte[32];
    private readonly SecureSecret _protectedPinKey;

    private const string TestSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";

    public RepairingAppEntryLauncherTests()
    {
        _sessionProvider = new Mock<ISessionProvider>();
        _database = new AppDatabase
        {
            SidNames = { [TestSid] = "User" }
        };
        _protectedPinKey = TestSecretFactory.FromBytes(_pinDerivedKey);
        _sessionProvider.Setup(s => s.GetSession()).Returns(new SessionContext
        {
            Database = _database,
            CredentialStore = new CredentialStore(),
        }.WithClonedPinDerivedKey(_protectedPinKey));

        _iconService.Setup(service => service.CreateBadgedIcon(It.IsAny<AppEntry>())).Returns("icon.ico");
        _sidNameCache.Setup(cache => cache.GetDisplayName(It.IsAny<string>())).Returns<string>(sid => sid);
        _launchExecutor.Setup(executor => executor.Launch(
                It.IsAny<AppEntry>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<Func<string, string, bool>?>(),
                It.IsAny<string?>()))
            .Returns(new LaunchExecutionResult(LaunchExecutionStatus.ProcessStarted, null, []));
    }

    public void Dispose() => _protectedPinKey.Dispose();

    private RepairingAppEntryLauncher BuildLauncher(
        IBackupIntentFileSystem? fileSystem = null,
        IReadOnlyList<string>? programFilesRoots = null,
        IReadOnlyDictionary<string, string>? profilePaths = null)
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

        var committer = new AppEntryPathRepairCommitter(
            _sessionProvider.Object,
            _appConfigService.Object,
            _iconService.Object,
            enforcementCoordinator,
            _aclService.Object);
        var coordinator = new AppEntryPathRepairCoordinator(
            new VersionedPathAutoRepairTrustPolicy(programFilesProvider.Object, profilePathResolver.Object),
            new VersionedPathRepairer(fileSystem ?? new TestBackupIntentFileSystem()),
            new VersionedPathRepairOptionsBuilder(profilePathResolver.Object),
            committer);
        var uiService = new UiThreadAppEntryPathRepairService(
            new UiThreadDatabaseAccessor(
                new LambdaDatabaseProvider(() => _database),
                () => new InlineUiThreadInvoker(action => action())),
            coordinator);

        return new RepairingAppEntryLauncher(
            _sessionProvider.Object,
            uiService,
            _launchExecutor.Object);
    }

    [Fact]
    public void Launch_UrlSchemeApp_DelegatesWithoutRepair()
    {
        var app = new AppEntry { Id = "url01", AccountSid = TestSid, ExePath = "https://example.com", IsUrlScheme = true };
        var launcher = BuildLauncher();

        launcher.Launch(app, "--arg");

        _launchExecutor.Verify(executor => executor.Launch(
            app,
            "--arg",
            null,
            null,
            null), Times.Once);
        _appConfigService.Verify(service => service.SaveConfigForApp(
            It.IsAny<string>(),
            It.IsAny<AppDatabase>(),
            It.IsAny<ISecureSecretSnapshotSource>(),
            It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public void Launch_ForwardsAllOptionalLaunchParameters()
    {
        var app = new AppEntry
        {
            Id = string.Empty,
            AccountSid = TestSid,
            ExePath = @"C:\Tools\NoRepair\App.exe"
        };
        var launcher = BuildLauncher();
        const string launcherArguments = "--launch-mode=fast";
        const string launcherWorkingDirectory = @"C:\Tools\Working";
        Func<string, string, bool> permissionPrompt = (_, _) => false;
        const string associationArgsTemplate = "\"%1\" --trusted";

        launcher.Launch(app, launcherArguments, launcherWorkingDirectory, permissionPrompt, associationArgsTemplate);

        _launchExecutor.Verify(executor => executor.Launch(
            It.Is<AppEntry>(candidate => ReferenceEquals(candidate, app)),
            launcherArguments,
            launcherWorkingDirectory,
            It.Is<Func<string, string, bool>>(prompt => ReferenceEquals(prompt, permissionPrompt)),
            associationArgsTemplate), Times.Once);
    }

    [Fact]
    public void Launch_MissingLiveApp_DelegatesOriginalAppWithoutRepair()
    {
        var app = new AppEntry { Id = "missing01", AccountSid = TestSid, ExePath = @"C:\Program Files\Vendor\App-1.0\App.exe" };
        var launcher = BuildLauncher();

        launcher.Launch(app, null);

        _launchExecutor.Verify(executor => executor.Launch(
            app,
            null,
            null,
            null,
            null), Times.Once);
        _appConfigService.Verify(service => service.SaveConfigForApp(
            It.IsAny<string>(),
            It.IsAny<AppDatabase>(),
            It.IsAny<ISecureSecretSnapshotSource>(),
            It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public void Launch_TrustedMissingPathWithNoCandidate_UsesOriginalPath()
    {
        var app = new AppEntry
        {
            Id = "pf001",
            AccountSid = TestSid,
            ExePath = @"C:\Program Files\Vendor\App-1.0\App.exe"
        };
        _database.Apps.Add(app);
        var launcher = BuildLauncher(
            new TestBackupIntentFileSystem()
                .WithMissingFile(app.ExePath)
                .WithExistingDirectory(@"C:\Program Files\Vendor")
                .WithEnumeratedDirectories(@"C:\Program Files\Vendor", [@"C:\Program Files\Vendor\App-1.1"]),
            programFilesRoots: [@"C:\Program Files"]);

        launcher.Launch(app, null);

        _launchExecutor.Verify(executor => executor.Launch(
            It.Is<AppEntry>(candidate => candidate.ExePath == app.ExePath),
            null,
            null,
            null,
            null), Times.Once);
        _appConfigService.Verify(service => service.SaveConfigForApp(
            It.IsAny<string>(),
            It.IsAny<AppDatabase>(),
            It.IsAny<ISecureSecretSnapshotSource>(),
            It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public void Launch_UntrustedMissingPath_DoesNotProbeOrPersist()
    {
        var fileSystem = new TestBackupIntentFileSystem();
        var app = new AppEntry
        {
            Id = "drv01",
            AccountSid = TestSid,
            ExePath = @"D:\Apps\Vendor\App-1.0\App.exe"
        };
        _database.Apps.Add(app);
        var launcher = BuildLauncher(fileSystem, programFilesRoots: [@"C:\Program Files"]);

        launcher.Launch(app, null);

        _launchExecutor.Verify(executor => executor.Launch(
            It.Is<AppEntry>(candidate => candidate.ExePath == app.ExePath),
            null,
            null,
            null,
            null), Times.Once);
        Assert.Equal(0, fileSystem.FileStateCallCount);
        Assert.Equal(0, fileSystem.DirectoryStateCallCount);
        _appConfigService.Verify(service => service.SaveConfigForApp(
            It.IsAny<string>(),
            It.IsAny<AppDatabase>(),
            It.IsAny<ISecureSecretSnapshotSource>(),
            It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public void Launch_RepairSaveFailure_UsesOriginalPath()
    {
        const string repairedPath = @"C:\Program Files\Vendor\App-1.1\App.exe";
        var app = new AppEntry
        {
            Id = "pf002",
            AccountSid = TestSid,
            ExePath = @"C:\Program Files\Vendor\App-1.0\App.exe"
        };
        _database.Apps.Add(app);
        _appConfigService.Setup(service => service.SaveConfigForApp(
                It.IsAny<string>(),
                It.IsAny<AppDatabase>(),
                It.IsAny<ISecureSecretSnapshotSource>(),
                It.IsAny<byte[]>()))
            .Throws(new InvalidOperationException("save failed"));
        var launcher = BuildLauncher(
            new TestBackupIntentFileSystem()
                .WithMissingFile(app.ExePath)
                .WithExistingDirectory(@"C:\Program Files\Vendor")
                .WithEnumeratedDirectories(@"C:\Program Files\Vendor", [@"C:\Program Files\Vendor\App-1.1"])
                .WithExistingFile(repairedPath),
            programFilesRoots: [@"C:\Program Files"]);

        launcher.Launch(app, null);

        _launchExecutor.Verify(executor => executor.Launch(
            It.Is<AppEntry>(candidate => candidate.ExePath == app.ExePath),
            null,
            null,
            null,
            null), Times.Once);
        Assert.Equal(app.ExePath, _database.Apps.Single(candidate => candidate.Id == app.Id).ExePath);
    }

    [Fact]
    public void Launch_TrustedRepairSuccess_UsesRepairedPath()
    {
        const string repairedPath = @"C:\Program Files\Vendor\App-1.1\App.exe";
        var app = new AppEntry
        {
            Id = "pf003",
            AccountSid = TestSid,
            ExePath = @"C:\Program Files\Vendor\App-1.0\App.exe"
        };
        _database.Apps.Add(app);
        var launcher = BuildLauncher(
            new TestBackupIntentFileSystem()
                .WithMissingFile(app.ExePath)
                .WithExistingDirectory(@"C:\Program Files\Vendor")
                .WithEnumeratedDirectories(@"C:\Program Files\Vendor", [@"C:\Program Files\Vendor\App-1.1"])
                .WithExistingFile(repairedPath),
            programFilesRoots: [@"C:\Program Files"]);

        launcher.Launch(app, null);

        _launchExecutor.Verify(executor => executor.Launch(
            It.Is<AppEntry>(candidate => candidate.ExePath == repairedPath),
            null,
            null,
            null,
            null), Times.Once);
        _appConfigService.Verify(service => service.SaveConfigForApp(
            app.Id,
            _database,
            It.IsAny<ISecureSecretSnapshotSource>(),
            It.IsAny<byte[]>()), Times.Once);
    }

    private sealed class TestBackupIntentFileSystem : IBackupIntentFileSystem
    {
        private readonly Dictionary<string, BackupIntentPathState> _fileStates = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, BackupIntentPathState> _directoryStates = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IReadOnlyList<string>> _enumerations = new(StringComparer.OrdinalIgnoreCase);

        public int FileStateCallCount { get; private set; }

        public int DirectoryStateCallCount { get; private set; }

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
