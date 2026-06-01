using System.Security.AccessControl;
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
using RunFence.Launch.Container;
using RunFence.Launch;
using RunFence.Persistence;
using RunFence.Startup;
using Xunit;

namespace RunFence.Tests;

public class StartupEnforcementRunnerTests
{
    private const string ContainerName = "ram_testcontainer";
    private const string ContainerSid = "S-1-15-2-1-2-3-4-5-6-7";
    private const string UserSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";

    private readonly Mock<IStartupEnforcementService> _enforcementService = new();
    private readonly Mock<ISessionSaver> _sessionSaver = new();
    private readonly Mock<IAppContainerService> _appContainerService = new();
    private readonly Mock<IAclPermissionService> _aclPermission = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<IGrantMutatorService> _grantMutatorService = new();
    private readonly Mock<IInteractiveUserResolver> _interactiveUserResolver = new();
    private readonly Mock<IAppConfigService> _appConfigService = new();
    private readonly Mock<IAclService> _aclService = new();
    private readonly Mock<IShortcutService> _shortcutService = new();
    private readonly Mock<IBesideTargetShortcutService> _besideTargetShortcutService = new();
    private readonly Mock<IIconService> _iconService = new();
    private readonly Mock<ISidNameCacheService> _sidNameCache = new();
    private readonly Mock<IInteractiveUserDesktopProvider> _desktopProvider = new();
    private readonly Mock<IInteractiveUserSidResolver> _interactiveUserSidResolver = new();
    private readonly TestFileSystemPathInfo _pathInfo = new();

    private AppDatabase _database = new();

    public StartupEnforcementRunnerTests()
    {
        // Default: HasEffectiveRights returns true — no real NTFS ACE writes during reconciliation.
        _aclPermission.Setup(s => s.HasEffectiveRights(
                It.IsAny<FileSystemSecurity>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<FileSystemRights>()))
            .Returns(true);

        _appContainerService.Setup(s => s.GetSid(ContainerName)).Returns(ContainerSid);
        _interactiveUserResolver.Setup(r => r.GetInteractiveUserSid()).Returns(UserSid);
        _iconService.Setup(service => service.CreateBadgedIcon(It.IsAny<AppEntry>())).Returns("icon.ico");
        _sidNameCache.Setup(cache => cache.GetDisplayName(It.IsAny<string>())).Returns<string>(sid => sid);
    }

    private StartupEnforcementRunner BuildRunner(AppEntryPathRepairCoordinator? repairCoordinator = null)
    {
        var sessionProvider = CreateSessionProvider();
        var sidReconciler = new SidReconciler(
            _aclPermission.Object,
            () => new AncestorTraverseGranter(_log.Object, _aclPermission.Object, new Mock<ITraverseAcl>().Object,
                _pathInfo),
            _log.Object,
            new Mock<IInteractiveUserResolver>().Object,
            _pathInfo,
            new Mock<IProgramDataDirectoryProvisioningService>().Object,
            Mock.Of<IProgramDataKnownPathResolver>());
        var reconciliationService = new GrantReconciliationService(
            _aclPermission.Object, new Mock<ILocalGroupQueryService>().Object, _log.Object, _sessionSaver.Object,
            new LambdaDatabaseProvider(() => _database),
            sidReconciler);
        var enforcementResultApplier = new EnforcementResultApplier(
            _appContainerService.Object,
            new TraverseGrantOwnerResolver());
        var stubPersistenceHelper = new SessionPersistenceHelper(
            new Mock<IConfigReencryptionPersistence>().Object, new Mock<IMainConfigPersistence>().Object,
            new Mock<ISidNameCacheService>().Object,
            () => new InlineUiThreadInvoker(action => action()),
            new Mock<ILoggingService>().Object);
        var ephemeralAccountService = new EphemeralAccountService(
            new Mock<IAccountDeletionService>().Object,
            stubPersistenceHelper,
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
            _enforcementService.Object,
            sessionProvider,
            _sessionSaver.Object,
            _grantMutatorService.Object,
            ephemeralAccountService,
            ephemeralContainerService,
            reconciliationService,
            _appContainerService.Object,
            _interactiveUserResolver.Object,
            _log.Object,
            enforcementResultApplier,
            repairCoordinator ?? CreateRepairCoordinator(sessionProvider, new TestBackupIntentFileSystem()));
    }

    private AppEntryPathRepairCoordinator CreateRepairCoordinator(
        ISessionProvider sessionProvider,
        IBackupIntentFileSystem fileSystem,
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

    // --- RefreshContainerSidsIfUserChanged tests ---

    [Fact]
    public void RefreshContainerSidsIfUserChanged_WhenInteractiveUserChanged_RewritesContainerSidsAndPersistsLastInteractiveUserSid()
    {
        _database.AppContainers =
        [
            new AppContainerEntry { Name = ContainerName, Sid = "stale-sid" }
        ];
        _database.Settings.LastInteractiveUserSid = "S-1-5-21-previous";

        var runner = BuildRunner();

        runner.RefreshContainerSidsIfUserChanged();

        Assert.Equal(ContainerSid, _database.AppContainers[0].Sid);
        Assert.Equal(UserSid, _database.Settings.LastInteractiveUserSid);
        _appContainerService.Verify(s => s.GetSid(ContainerName), Times.Once);
        _sessionSaver.Verify(s => s.SaveConfig(), Times.Once);
    }

    [Fact]
    public void RefreshContainerSidsIfUserChanged_WhenInteractiveUserUnchanged_DoesNothing()
    {
        _database.AppContainers =
        [
            new AppContainerEntry { Name = ContainerName, Sid = "cached-sid" }
        ];
        _database.Settings.LastInteractiveUserSid = UserSid;

        var runner = BuildRunner();

        runner.RefreshContainerSidsIfUserChanged();

        Assert.Equal("cached-sid", _database.AppContainers[0].Sid);
        _appContainerService.Verify(s => s.GetSid(It.IsAny<string>()), Times.Never);
        _sessionSaver.Verify(s => s.SaveConfig(), Times.Never);
    }

    [Fact]
    public void RefreshContainerSidsIfUserChanged_WhenInteractiveUserUnavailable_DoesNothing()
    {
        _database.AppContainers =
        [
            new AppContainerEntry { Name = ContainerName, Sid = "cached-sid" }
        ];
        _database.Settings.LastInteractiveUserSid = "S-1-5-21-previous";
        _interactiveUserResolver.Setup(r => r.GetInteractiveUserSid()).Returns((string?)null);

        var runner = BuildRunner();

        runner.RefreshContainerSidsIfUserChanged();

        Assert.Equal("cached-sid", _database.AppContainers[0].Sid);
        Assert.Equal("S-1-5-21-previous", _database.Settings.LastInteractiveUserSid);
        _appContainerService.Verify(s => s.GetSid(It.IsAny<string>()), Times.Never);
        _sessionSaver.Verify(s => s.SaveConfig(), Times.Never);
    }

    // --- ApplyEnforcementResult tests ---

    [Fact]
    public async Task ApplyEnforcementResult_TimestampUpdates_AppliedToMatchingApps()
    {
        // Arrange
        var app1 = new AppEntry { Id = "app-1", Name = "App1" };
        var app2 = new AppEntry { Id = "app-2", Name = "App2" };
        _database.Apps = [app1, app2];

        var now = DateTime.UtcNow;
        var result = new EnforcementResult(
            TimestampUpdates: new Dictionary<string, DateTime> { ["app-1"] = now },
            TraverseGrants: []);

        var runner = BuildRunner();

        // Act
        await runner.ApplyEnforcementResult(result);

        // Assert
        Assert.Equal(now, app1.LastKnownExeTimestamp);
        Assert.Null(app2.LastKnownExeTimestamp);
    }

    [Fact]
    public async Task ApplyEnforcementResult_TimestampUpdatesNonEmpty_ReconciliationReturnedFalse_CallsSaveConfig()
    {
        var app = new AppEntry { Id = "app-ts", Name = "TimestampApp" };
        _database.Apps = [app];

        // No SIDs in database → ReconcileIfGroupsChanged returns false
        var result = new EnforcementResult(
            TimestampUpdates: new Dictionary<string, DateTime> { ["app-ts"] = DateTime.UtcNow },
            TraverseGrants: []);

        var runner = BuildRunner();

        await runner.ApplyEnforcementResult(result);

        _sessionSaver.Verify(s => s.SaveConfig(), Times.Once);
    }

    [Fact]
    public async Task ApplyEnforcementResult_TraverseGrantsNonEmpty_ReconciliationReturnedFalse_CallsSaveConfig()
    {
        // Arrange
        var container = new AppContainerEntry { Name = ContainerName, Sid = ContainerSid };
        var traverseDir = @"C:\SomeDir";

        _database = new AppDatabase();
        var appliedPaths = new List<string> { traverseDir };
        var result = new EnforcementResult(
            TimestampUpdates: new Dictionary<string, DateTime>(),
            TraverseGrants: [new ContainerTraverseGrant(container, traverseDir, appliedPaths)]);

        var runner = BuildRunner();

        // Act
        await runner.ApplyEnforcementResult(result);

        // Assert: traverse was re-tracked → SaveConfig called
        _sessionSaver.Verify(s => s.SaveConfig(), Times.Once);
    }

    [Fact]
    public async Task ApplyEnforcementResult_EmptyUpdatesAndGrants_ReconciliationReturnedFalse_DoesNotCallSaveConfig()
    {
        var result = new EnforcementResult(
            TimestampUpdates: new Dictionary<string, DateTime>(),
            TraverseGrants: []);

        var runner = BuildRunner();

        await runner.ApplyEnforcementResult(result);

        _sessionSaver.Verify(s => s.SaveConfig(), Times.Never);
    }

    [Fact]
    public async Task ApplyEnforcementResult_UnknownAppId_DoesNotThrow()
    {
        _database.Apps = [new AppEntry { Id = "known-app", Name = "KnownApp" }];

        var result = new EnforcementResult(
            TimestampUpdates: new Dictionary<string, DateTime> { ["nonexistent-id"] = DateTime.UtcNow },
            TraverseGrants: []);

        var runner = BuildRunner();

        // Should not throw — unknown IDs are silently ignored
        await runner.ApplyEnforcementResult(result);
    }

    [Fact]
    public async Task ApplyEnforcementResult_TraverseGrantsNonEmpty_ReTracksOnLiveDatabase()
    {
        // Arrange
        var container = new AppContainerEntry { Name = ContainerName, Sid = ContainerSid };
        var traverseDir = @"C:\TraverseTarget";
        var appliedPaths = new List<string> { traverseDir, @"C:\" };

        _database = new AppDatabase();
        var result = new EnforcementResult(
            TimestampUpdates: new Dictionary<string, DateTime>(),
            TraverseGrants: [new ContainerTraverseGrant(container, traverseDir, appliedPaths)]);

        var runner = BuildRunner();

        // Act
        await runner.ApplyEnforcementResult(result);

        // Assert: the traverse path is now tracked under ALL APPLICATION PACKAGES.
        var grant = _database.GetAccount(AclHelper.AllApplicationPackagesSid)?.Grants.SingleOrDefault(g => g.IsTraverseOnly);
        Assert.NotNull(grant);
        Assert.Equal(Path.GetFullPath(traverseDir), Path.GetFullPath(grant.Path));
        Assert.Equal(appliedPaths, grant.AllAppliedPaths);
    }

    [Fact]
    public async Task ApplyEnforcementResult_ExistingTraverseGrant_RefreshesAppliedPathsOnLiveDatabase()
    {
        var container = new AppContainerEntry { Name = ContainerName, Sid = ContainerSid };
        var traverseDir = @"C:\TraverseTarget";
        var freshAppliedPaths = new List<string> { traverseDir, @"C:\" };
        _database.GetOrCreateAccount(AclHelper.AllApplicationPackagesSid).Grants.Add(new GrantedPathEntry
        {
            Path = traverseDir,
            IsTraverseOnly = true,
            AllAppliedPaths = [@"C:\Old"],
            SourceSids = [ContainerSid]
        });
        var result = new EnforcementResult(
            TimestampUpdates: new Dictionary<string, DateTime>(),
            TraverseGrants: [new ContainerTraverseGrant(container, traverseDir, freshAppliedPaths)]);

        var runner = BuildRunner();

        await runner.ApplyEnforcementResult(result);

        var grant = Assert.Single(_database.GetAccount(AclHelper.AllApplicationPackagesSid)!.Grants);
        Assert.Equal(freshAppliedPaths, grant.AllAppliedPaths);
        Assert.Equal([ContainerSid], grant.SourceSids);
        _sessionSaver.Verify(s => s.SaveConfig(), Times.Once);
    }

    [Fact]
    public async Task ApplyEnforcementResult_ReconciliationReturnedTrue_SkipsOwnSaveConfig()
    {
        // Arrange: set up a SID with a snapshot that differs from current groups,
        // so ReconcileIfGroupsChanged detects a change and returns true (auto-saves internally).
        _database.SidNames[UserSid] = "MACHINE\\testuser";
        _database.AccountGroupSnapshots = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [UserSid] = ["S-1-1-0"] // old snapshot
        };

        // Current groups differ → reconciliation will detect a change
        _aclPermission.Setup(s => s.ResolveAccountGroupSids(UserSid))
            .Returns(["S-1-1-0", "S-1-5-11"]); // new groups

        var result = new EnforcementResult(
            TimestampUpdates: new Dictionary<string, DateTime> { ["any-id"] = DateTime.UtcNow },
            TraverseGrants: []);

        var runner = BuildRunner();

        // Act
        await runner.ApplyEnforcementResult(result);

        // Assert: SaveConfig was called exactly once — from reconciliation's auto-save,
        // not an additional call from ApplyEnforcementResult itself.
        _sessionSaver.Verify(s => s.SaveConfig(), Times.Once);
    }

    [Fact]
    public async Task ApplyEnforcementResult_ContainerWithEmptySid_FallsBackToGetSid()
    {
        // Arrange: container has no cached Sid — applier must call GetSid(Name) to resolve it
        var container = new AppContainerEntry { Name = ContainerName, Sid = "" };
        var traverseDir = @"C:\FallbackDir";
        var appliedPaths = new List<string> { traverseDir };

        _database = new AppDatabase();
        var result = new EnforcementResult(
            TimestampUpdates: new Dictionary<string, DateTime>(),
            TraverseGrants: [new ContainerTraverseGrant(container, traverseDir, appliedPaths)]);

        var runner = BuildRunner();

        // Act
        await runner.ApplyEnforcementResult(result);

        // Assert: GetSid was called and the traverse path was tracked under ALL APPLICATION PACKAGES.
        _appContainerService.Verify(s => s.GetSid(ContainerName), Times.Once);
        Assert.Single(_database.GetAccount(AclHelper.AllApplicationPackagesSid)!.Grants, g => g.IsTraverseOnly);
    }

    // --- FixAppEntryDefaults tests ---

    [Fact]
    public void FixAppEntryDefaults_FolderApp_WithFileAclTarget_FixesToFolder()
    {
        var folderApp = new AppEntry
        {
            Id = "folder-app",
            Name = "FolderApp",
            IsFolder = true,
            AclTarget = AclTarget.File
        };
        _database.Apps = [folderApp];

        var runner = BuildRunner();

        var result = runner.FixAppEntryDefaults();

        Assert.Equal(AclTarget.Folder, folderApp.AclTarget);
        Assert.True(result.Changed);
        Assert.Contains("folder-app", result.ChangedAppIds);
    }

    [Fact]
    public void FixAppEntryDefaults_DenyModeApp_ClearsAllowedAclEntries()
    {
        var denyApp = new AppEntry
        {
            Name = "DenyApp",
            AclMode = AclMode.Deny,
            AllowedAclEntries = [new AllowAclEntry { Sid = "S-1-5-21-111-222-333-1001" }]
        };
        _database.Apps = [denyApp];

        var runner = BuildRunner();

        runner.FixAppEntryDefaults();

        Assert.Null(denyApp.AllowedAclEntries);
    }

    [Fact]
    public void FixAppEntryDefaults_AllowModeApp_AndNonFolderApp_NotModified()
    {
        var allowEntry = new AllowAclEntry { Sid = "S-1-5-21-111-222-333-1002" };
        var allowApp = new AppEntry
        {
            Name = "AllowApp",
            AclMode = AclMode.Allow,
            IsFolder = false,
            AclTarget = AclTarget.File,
            AllowedAclEntries = [allowEntry]
        };
        var nonFolderApp = new AppEntry
        {
            Name = "ExeApp",
            IsFolder = false,
            AclTarget = AclTarget.File
        };
        _database.Apps = [allowApp, nonFolderApp];

        var runner = BuildRunner();

        runner.FixAppEntryDefaults();

        // Allow-mode app: AllowedAclEntries must NOT be cleared
        Assert.NotNull(allowApp.AllowedAclEntries);
        Assert.Same(allowEntry, allowApp.AllowedAclEntries![0]);

        // Non-folder app: AclTarget must NOT be changed
        Assert.Equal(AclTarget.File, nonFolderApp.AclTarget);
    }

    [Fact]
    public void GrantUnlockDirAccess_EnsuresAccessForInteractiveUserAndLowIntegrity()
    {
        var runner = BuildRunner();

        runner.GrantUnlockDirAccess();

        var unlockDir = Path.GetDirectoryName(PathConstants.UnlockCmdPath)!;
        _grantMutatorService.Verify(g => g.EnsureAccess(
            UserSid, unlockDir, FileSystemRights.ReadAndExecute,
            null, true), Times.Once);
        _grantMutatorService.Verify(g => g.EnsureAccess(
            AclHelper.LowIntegritySid, unlockDir, FileSystemRights.ReadAndExecute,
            null, true), Times.Once);
        _grantMutatorService.Verify(g => g.EnsureAccess(
            AclHelper.AllApplicationPackagesSid, unlockDir, FileSystemRights.ReadAndExecute,
            null, true), Times.Once);
        _sessionSaver.Verify(s => s.SaveConfig(), Times.Never);
    }

    [Fact]
    public void GrantUnlockDirAccess_WhenInteractiveUserUnavailable_DoesNotGrantAccess()
    {
        _interactiveUserResolver.Setup(r => r.GetInteractiveUserSid()).Returns((string?)null);
        var runner = BuildRunner();

        runner.GrantUnlockDirAccess();

        _grantMutatorService.Verify(g => g.EnsureAccess(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<FileSystemRights>(),
            It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public void RepairMissingAppEntryPaths_UntrustedPath_DoesNotProbeOrPersist()
    {
        var fileSystem = new TestBackupIntentFileSystem();
        var app = new AppEntry
        {
            Id = "app-untrusted",
            ExePath = @"D:\Apps\Vendor\App-1.0\App.exe"
        };
        _database.Apps = [app];

        var sessionProvider = CreateSessionProvider();
        var runner = BuildRunner(CreateRepairCoordinator(
            sessionProvider,
            fileSystem,
            programFilesRoots: [@"C:\Program Files"]));

        var result = runner.RepairMissingAppEntryPaths();

        Assert.False(result.Changed);
        Assert.Null(result.SaveFailureMessage);
        Assert.Empty(result.ChangedAppIds);
        Assert.Equal(0, fileSystem.FileStateCallCount);
        Assert.Equal(0, fileSystem.DirectoryStateCallCount);
        _appConfigService.Verify(service => service.SaveConfigForApp(
            It.IsAny<string>(),
            It.IsAny<AppDatabase>(),
            It.IsAny<ISecureSecretSnapshotSource>(),
            It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public void RepairMissingAppEntryPaths_TrustedPathWithNoCandidate_ReturnsUnchanged()
    {
        var app = new AppEntry
        {
            Id = "app-trusted",
            ExePath = @"C:\Program Files\Vendor\App-1.0\App.exe"
        };
        _database.Apps = [app];

        var sessionProvider = CreateSessionProvider();
        var runner = BuildRunner(CreateRepairCoordinator(
            sessionProvider,
            new TestBackupIntentFileSystem()
                .WithMissingFile(app.ExePath)
                .WithExistingDirectory(@"C:\Program Files\Vendor")
                .WithEnumeratedDirectories(@"C:\Program Files\Vendor", [@"C:\Program Files\Vendor\App-1.1"]),
            programFilesRoots: [@"C:\Program Files"]));

        var result = runner.RepairMissingAppEntryPaths();

        Assert.False(result.Changed);
        Assert.Null(result.SaveFailureMessage);
        Assert.Equal(app.ExePath, _database.Apps.Single().ExePath);
        _appConfigService.Verify(service => service.SaveConfigForApp(
            It.IsAny<string>(),
            It.IsAny<AppDatabase>(),
            It.IsAny<ISecureSecretSnapshotSource>(),
            It.IsAny<byte[]>()), Times.Never);
        _aclService.Verify(service => service.ApplyAcl(It.IsAny<AppEntry>(), It.IsAny<IReadOnlyList<AppEntry>>()), Times.Never);
    }

    [Fact]
    public void RepairMissingAppEntryPaths_SaveFailure_ReturnsSaveFailureMessageAndRestoresOriginalPath()
    {
        var app = new AppEntry
        {
            Id = "app-fail",
            Name = "RepairTarget",
            ExePath = @"C:\Program Files\Vendor\App-1.0\App.exe"
        };
        _database.Apps = [app];
        _appConfigService.Setup(service => service.SaveConfigForApp(
                It.IsAny<string>(),
                It.IsAny<AppDatabase>(),
                It.IsAny<ISecureSecretSnapshotSource>(),
                It.IsAny<byte[]>()))
            .Throws(new InvalidOperationException("save failed"));

        var sessionProvider = CreateSessionProvider();
        var runner = BuildRunner(CreateRepairCoordinator(
            sessionProvider,
            new TestBackupIntentFileSystem()
                .WithMissingFile(app.ExePath)
                .WithExistingDirectory(@"C:\Program Files\Vendor")
                .WithEnumeratedDirectories(@"C:\Program Files\Vendor", [@"C:\Program Files\Vendor\App-1.1"])
                .WithExistingFile(@"C:\Program Files\Vendor\App-1.1\App.exe"),
            programFilesRoots: [@"C:\Program Files"]));

        var result = runner.RepairMissingAppEntryPaths();

        Assert.False(result.Changed);
        Assert.NotNull(result.SaveFailureMessage);
        Assert.Contains("RepairTarget", result.SaveFailureMessage, StringComparison.Ordinal);
        Assert.Equal(@"C:\Program Files\Vendor\App-1.0\App.exe", _database.Apps.Single().ExePath);
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

    private LambdaSessionProvider CreateSessionProvider()
    {
        var session = new SessionContext
        {
            Database = _database,
            CredentialStore = new CredentialStore()
        }.WithPinDerivedKeyTakingOwnership(TestSecretFactory.Create(32));

        return new LambdaSessionProvider(() => session);
    }
}
