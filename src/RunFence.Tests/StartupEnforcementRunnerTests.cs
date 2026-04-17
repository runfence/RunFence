using System.Security.AccessControl;
using Moq;
using RunFence.Acl;
using RunFence.Acl.Permissions;
using RunFence.Acl.Traverse;
using RunFence.Account;
using RunFence.Account.Lifecycle;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch.Container;
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

    private AppDatabase _database = new();

    public StartupEnforcementRunnerTests()
    {
        // Default: HasEffectiveRights returns true — no real NTFS ACE writes during reconciliation.
        _aclPermission.Setup(s => s.HasEffectiveRights(
                It.IsAny<FileSystemSecurity>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<FileSystemRights>()))
            .Returns(true);

        _appContainerService.Setup(s => s.GetSid(ContainerName)).Returns(ContainerSid);
    }

    private StartupEnforcementRunner BuildRunner()
    {
        var sessionProvider = new LambdaSessionProvider(() => new SessionContext { Database = _database });
        var reconciliationService = new GrantReconciliationService(
            _aclPermission.Object, new Mock<ILocalGroupMembershipService>().Object, _log.Object, _sessionSaver.Object,
            new LambdaDatabaseProvider(() => _database),
            () => new AncestorTraverseGranter(_log.Object, _aclPermission.Object, new Mock<ITraverseAcl>().Object),
            new Mock<IInteractiveUserResolver>().Object);
        var enforcementResultApplier = new EnforcementResultApplier(_appContainerService.Object);
        var stubPersistenceHelper = new SessionPersistenceHelper(
            new Mock<ICredentialRepository>().Object, new Mock<IConfigRepository>().Object,
            new Mock<ISidNameCacheService>().Object, new Mock<ILoggingService>().Object);
        var ephemeralAccountService = new EphemeralAccountService(
            new Mock<IAccountLifecycleManager>().Object,
            new Mock<IAccountDeletionService>().Object,
            stubPersistenceHelper,
            new Mock<ILocalUserProvider>().Object,
            _log.Object,
            new Mock<IAccountValidationService>().Object,
            sessionProvider,
            new Mock<IUiThreadInvoker>().Object,
            new Mock<ISidResolver>().Object,
            new Mock<IPathGrantService>().Object);

        var ephemeralContainerService = new EphemeralContainerService(
            new Mock<IContainerDeletionService>().Object,
            new Mock<IDatabaseService>().Object,
            _log.Object,
            sessionProvider,
            new Mock<IUiThreadInvoker>().Object,
            new Mock<IProcessListService>().Object);

        return new StartupEnforcementRunner(
            _enforcementService.Object,
            sessionProvider,
            _sessionSaver.Object,
            new Mock<IPathGrantService>().Object,
            ephemeralAccountService,
            ephemeralContainerService,
            reconciliationService,
            _appContainerService.Object,
            new Mock<IInteractiveUserResolver>().Object,
            _log.Object,
            enforcementResultApplier);
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

        // Assert: the traverse path is now tracked on the live database for the container SID
        var account = _database.GetAccount(ContainerSid);
        Assert.NotNull(account);
        var grant = account.Grants.SingleOrDefault(g => g.IsTraverseOnly);
        Assert.NotNull(grant);
        Assert.Equal(Path.GetFullPath(traverseDir), Path.GetFullPath(grant.Path));
        Assert.Equal(appliedPaths, grant.AllAppliedPaths);
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

        // Assert: GetSid was called and the traverse path was tracked under the resolved SID
        _appContainerService.Verify(s => s.GetSid(ContainerName), Times.Once);
        var account = _database.GetAccount(ContainerSid);
        Assert.NotNull(account);
        Assert.Single(account.Grants, g => g.IsTraverseOnly);
    }

    // --- FixAppEntryDefaults tests ---

    [Fact]
    public void FixAppEntryDefaults_FolderApp_WithFileAclTarget_FixesToFolder()
    {
        var folderApp = new AppEntry
        {
            Name = "FolderApp",
            IsFolder = true,
            AclTarget = AclTarget.File
        };
        _database.Apps = [folderApp];

        var runner = BuildRunner();

        runner.FixAppEntryDefaults();

        Assert.Equal(AclTarget.Folder, folderApp.AclTarget);
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
}