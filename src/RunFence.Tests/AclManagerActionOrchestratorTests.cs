using Moq;
using RunFence.Acl;
using RunFence.Acl.Permissions;
using RunFence.Acl.Traverse;
using RunFence.Acl.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public class AclManagerActionOrchestratorTests
{
    private const string TestSid = "S-1-5-21-111-222-333-1001";
    private const string ContainerSid = "S-1-15-2-99-1-2-3-4-5-6";

    [Fact]
    public void AddGrantPathDirect_ExistingBrokenGrant_QueuesPendingFix()
    {
        var path = Path.GetFullPath(@"C:\ExistingBroken");
        var rights = SavedRightsState.DefaultForMode(isDeny: true);
        var database = new AppDatabase();
        var existing = new GrantedPathEntry
        {
            Path = path,
            IsDeny = true,
            SavedRights = rights
        };
        database.GetOrCreateAccount(TestSid).Grants.Add(existing);

        var pending = new AclManagerPendingChanges();
        var pathGrantService = new Mock<IPathGrantService>();
        pathGrantService
            .Setup(s => s.ReadGrantState(path, TestSid, It.IsAny<IReadOnlyList<string>>()))
            .Returns(new GrantRightsState(
                AllowExecute: RightCheckState.Unchecked,
                AllowWrite: RightCheckState.Unchecked,
                AllowSpecial: RightCheckState.Unchecked,
                DenyRead: RightCheckState.Unchecked,
                DenyExecute: RightCheckState.Unchecked,
                DenyWrite: RightCheckState.Unchecked,
                DenySpecial: RightCheckState.Unchecked,
                TraverseOnlyAllow: RightCheckState.Unchecked,
                TraverseOnlyDeny: RightCheckState.Unchecked,
                IsAccountOwner: RightCheckState.Unchecked,
                IsAdminOwner: false,
                DirectAllowAceCount: 0,
                DirectDenyAceCount: 0));

        var refresher = new TestGridRefresher();
        var orchestrator = CreateOrchestrator(database, pathGrantService.Object, pending, refresher);

        var error = orchestrator.AddGrantPathDirect(path, isDeny: true);

        Assert.Null(error);
        var pendingFix = Assert.Single(pending.PendingGrantFixes).Value;
        Assert.Same(existing, pendingFix);
        Assert.Equal(1, refresher.GrantsRefreshCount);
    }

    [Fact]
    public void AddGrantPathDirect_ExistingBrokenGrantInDifferentSection_QueuesPendingFixAndConfigMove()
    {
        var path = Path.GetFullPath(@"C:\ExistingBrokenMoved");
        var targetConfigPath = Path.GetFullPath(@"C:\Configs\extra.rfn");
        var rights = SavedRightsState.DefaultForMode(isDeny: true);
        var database = new AppDatabase();
        var existing = new GrantedPathEntry
        {
            Path = path,
            IsDeny = true,
            SavedRights = rights
        };
        database.GetOrCreateAccount(TestSid).Grants.Add(existing);

        var pending = new AclManagerPendingChanges();
        var pathGrantService = new Mock<IPathGrantService>();
        pathGrantService
            .Setup(s => s.ReadGrantState(path, TestSid, It.IsAny<IReadOnlyList<string>>()))
            .Returns(new GrantRightsState(
                AllowExecute: RightCheckState.Unchecked,
                AllowWrite: RightCheckState.Unchecked,
                AllowSpecial: RightCheckState.Unchecked,
                DenyRead: RightCheckState.Unchecked,
                DenyExecute: RightCheckState.Unchecked,
                DenyWrite: RightCheckState.Unchecked,
                DenySpecial: RightCheckState.Unchecked,
                TraverseOnlyAllow: RightCheckState.Unchecked,
                TraverseOnlyDeny: RightCheckState.Unchecked,
                IsAccountOwner: RightCheckState.Unchecked,
                IsAdminOwner: false,
                DirectAllowAceCount: 0,
                DirectDenyAceCount: 0));

        var refresher = new TestGridRefresher();
        var additionalStore = new TestGrantIntentStore(targetConfigPath);
        var orchestrator = CreateOrchestrator(
            database,
            pathGrantService.Object,
            pending,
            refresher,
            loadedStores: [additionalStore]);

        var error = orchestrator.AddGrantPathDirect(
            path,
            isDeny: true,
            targetConfigPath: targetConfigPath,
            hasExplicitTargetSection: true);

        Assert.Null(error);
        var pendingFix = Assert.Single(pending.PendingGrantFixes).Value;
        Assert.Same(existing, pendingFix);
        Assert.Equal(targetConfigPath, pending.PendingConfigMoves[(path, true)].TargetConfigPath);
        Assert.Equal(1, refresher.GrantsRefreshCount);
    }

    [Fact]
    public void AddGrantPathDirect_ExistingBrokenGrantWithoutExplicitTarget_DoesNotMoveConfigSection()
    {
        var path = Path.GetFullPath(@"C:\ExistingBrokenNoTarget");
        var rights = SavedRightsState.DefaultForMode(isDeny: true);
        var database = new AppDatabase();
        var existing = new GrantedPathEntry
        {
            Path = path,
            IsDeny = true,
            SavedRights = rights
        };
        database.GetOrCreateAccount(TestSid).Grants.Add(existing);

        var pending = new AclManagerPendingChanges();
        var pathGrantService = new Mock<IPathGrantService>();
        pathGrantService
            .Setup(s => s.ReadGrantState(path, TestSid, It.IsAny<IReadOnlyList<string>>()))
            .Returns(new GrantRightsState(
                AllowExecute: RightCheckState.Unchecked,
                AllowWrite: RightCheckState.Unchecked,
                AllowSpecial: RightCheckState.Unchecked,
                DenyRead: RightCheckState.Unchecked,
                DenyExecute: RightCheckState.Unchecked,
                DenyWrite: RightCheckState.Unchecked,
                DenySpecial: RightCheckState.Unchecked,
                TraverseOnlyAllow: RightCheckState.Unchecked,
                TraverseOnlyDeny: RightCheckState.Unchecked,
                IsAccountOwner: RightCheckState.Unchecked,
                IsAdminOwner: false,
                DirectAllowAceCount: 0,
                DirectDenyAceCount: 0));

        var refresher = new TestGridRefresher();
        var additionalStore = new TestGrantIntentStore(@"C:\Configs\extra.rfn");
        additionalStore.AddEntry(TestSid, existing);
        var orchestrator = CreateOrchestrator(
            database,
            pathGrantService.Object,
            pending,
            refresher,
            loadedStores: [additionalStore]);

        var error = orchestrator.AddGrantPathDirect(path, isDeny: true);

        Assert.Null(error);
        var pendingFix = Assert.Single(pending.PendingGrantFixes).Value;
        Assert.Same(existing, pendingFix);
        Assert.Empty(pending.PendingConfigMoves);
        Assert.Equal(1, refresher.GrantsRefreshCount);
    }

    [Fact]
    public void AddGrantPathDirect_ExistingBrokenGrantWithExplicitMainTarget_MovesToMainConfig()
    {
        var path = Path.GetFullPath(@"C:\ExistingBrokenMainTarget");
        var rights = SavedRightsState.DefaultForMode(isDeny: true);
        var database = new AppDatabase();
        var existing = new GrantedPathEntry
        {
            Path = path,
            IsDeny = true,
            SavedRights = rights
        };
        database.GetOrCreateAccount(TestSid).Grants.Add(existing);

        var pending = new AclManagerPendingChanges();
        var pathGrantService = new Mock<IPathGrantService>();
        pathGrantService
            .Setup(s => s.ReadGrantState(path, TestSid, It.IsAny<IReadOnlyList<string>>()))
            .Returns(new GrantRightsState(
                AllowExecute: RightCheckState.Unchecked,
                AllowWrite: RightCheckState.Unchecked,
                AllowSpecial: RightCheckState.Unchecked,
                DenyRead: RightCheckState.Unchecked,
                DenyExecute: RightCheckState.Unchecked,
                DenyWrite: RightCheckState.Unchecked,
                DenySpecial: RightCheckState.Unchecked,
                TraverseOnlyAllow: RightCheckState.Unchecked,
                TraverseOnlyDeny: RightCheckState.Unchecked,
                IsAccountOwner: RightCheckState.Unchecked,
                IsAdminOwner: false,
                DirectAllowAceCount: 0,
                DirectDenyAceCount: 0));

        var refresher = new TestGridRefresher();
        var additionalStore = new TestGrantIntentStore(@"C:\Configs\extra.rfn");
        additionalStore.AddEntry(TestSid, existing);
        var orchestrator = CreateOrchestrator(
            database,
            pathGrantService.Object,
            pending,
            refresher,
            loadedStores: [additionalStore]);

        var error = orchestrator.AddGrantPathDirect(
            path,
            isDeny: true,
            targetConfigPath: null,
            hasExplicitTargetSection: true);

        Assert.Null(error);
        var pendingFix = Assert.Single(pending.PendingGrantFixes).Value;
        Assert.Same(existing, pendingFix);
        Assert.True(pending.PendingConfigMoves.ContainsKey((path, true)));
        Assert.Null(pending.PendingConfigMoves[(path, true)].TargetConfigPath);
        Assert.Equal(1, refresher.GrantsRefreshCount);
    }

    [Fact]
    public void AddGrantPathDirect_CanceledPendingRemove_WithExplicitMainTarget_PreservesConfigMoveIntent()
    {
        var path = Path.GetFullPath(@"C:\RestoreRemovedToMain");
        var rights = SavedRightsState.DefaultForMode(isDeny: true);
        var database = new AppDatabase();
        var existing = new GrantedPathEntry
        {
            Path = path,
            IsDeny = true,
            SavedRights = rights
        };
        database.GetOrCreateAccount(TestSid).Grants.Add(existing);

        var pending = new AclManagerPendingChanges();
        pending.PendingRemoves[(path, true)] = existing;
        var pathGrantService = new Mock<IPathGrantService>();
        var refresher = new TestGridRefresher();
        var additionalStore = new TestGrantIntentStore(@"C:\Configs\extra.rfn");
        additionalStore.AddEntry(TestSid, existing);
        var orchestrator = CreateOrchestrator(
            database,
            pathGrantService.Object,
            pending,
            refresher,
            loadedStores: [additionalStore]);

        var error = orchestrator.AddGrantPathDirect(
            path,
            isDeny: true,
            targetConfigPath: null,
            hasExplicitTargetSection: true);

        Assert.Null(error);
        Assert.Empty(pending.PendingRemoves);
        Assert.True(pending.PendingConfigMoves.ContainsKey((path, true)));
        Assert.Null(pending.PendingConfigMoves[(path, true)].TargetConfigPath);
        Assert.Equal(1, refresher.GrantsRefreshCount);
    }

    [Fact]
    public void AddGrantPathDirect_ExistingHealthyGrant_ReturnsDuplicate()
    {
        var path = Path.GetFullPath(@"C:\ExistingHealthy");
        var rights = SavedRightsState.DefaultForMode(isDeny: true);
        var database = new AppDatabase();
        database.GetOrCreateAccount(TestSid).Grants.Add(new GrantedPathEntry
        {
            Path = path,
            IsDeny = true,
            SavedRights = rights
        });

        var pending = new AclManagerPendingChanges();
        var pathGrantService = new Mock<IPathGrantService>();
        pathGrantService
            .Setup(s => s.ReadGrantState(path, TestSid, It.IsAny<IReadOnlyList<string>>()))
            .Returns(new GrantRightsState(
                AllowExecute: RightCheckState.Unchecked,
                AllowWrite: RightCheckState.Unchecked,
                AllowSpecial: RightCheckState.Unchecked,
                DenyRead: RightCheckState.Unchecked,
                DenyExecute: RightCheckState.Unchecked,
                DenyWrite: RightCheckState.Checked,
                DenySpecial: RightCheckState.Checked,
                TraverseOnlyAllow: RightCheckState.Unchecked,
                TraverseOnlyDeny: RightCheckState.Unchecked,
                IsAccountOwner: RightCheckState.Unchecked,
                IsAdminOwner: false,
                DirectAllowAceCount: 0,
                DirectDenyAceCount: 1));

        var orchestrator = CreateOrchestrator(database, pathGrantService.Object, pending, new TestGridRefresher());

        var error = orchestrator.AddGrantPathDirect(path, isDeny: true);

        Assert.Equal("This path is already in the list.", error);
        Assert.Empty(pending.PendingGrantFixes);
    }

    [Fact]
    public void AddGrantPathDirect_LowIntegrityAllowWithSpecificContainerAce_ReturnsConflictWarning()
    {
        var path = Path.GetFullPath(@"C:\ContainerConflict");
        var database = new AppDatabase();
        var pending = new AclManagerPendingChanges();
        var containerAceDetector = new Mock<ISpecificContainerAceConflictDetector>();
        containerAceDetector
            .Setup(d => d.HasExplicitSpecificContainerAce(path))
            .Returns(true);

        var orchestrator = CreateOrchestrator(
            database,
            new Mock<IPathGrantService>().Object,
            pending,
            new TestGridRefresher(),
            AclHelper.LowIntegritySid,
            containerAceDetector.Object);

        var error = orchestrator.AddGrantPathDirect(path, isDeny: false);

        Assert.Contains("Specific AppContainer ACEs conflict", error);
        Assert.Empty(pending.PendingAdds);
    }

    [Fact]
    public void AddGrantPathDirect_SpecificContainerAllowWithLowIntegrityAce_ReturnsConflictWarning()
    {
        var path = Path.GetFullPath(@"C:\LowIntegrityConflict");
        var database = new AppDatabase();
        var pending = new AclManagerPendingChanges();
        var containerAceDetector = new Mock<ISpecificContainerAceConflictDetector>();
        containerAceDetector
            .Setup(d => d.HasLowIntegrityAce(path))
            .Returns(true);

        var orchestrator = CreateOrchestrator(
            database,
            new Mock<IPathGrantService>().Object,
            pending,
            new TestGridRefresher(),
            ContainerSid,
            containerAceDetector.Object);

        var error = orchestrator.AddGrantPathDirect(path, isDeny: false);

        Assert.Contains("ordinary Low Integrity access stop working", error);
        Assert.Empty(pending.PendingAdds);
    }

    private static AclManagerActionOrchestrator CreateOrchestrator(
        AppDatabase database,
        IPathGrantService pathGrantService,
        AclManagerPendingChanges pending,
        IAclManagerGridRefresher refresher,
        string sid = TestSid,
        ISpecificContainerAceConflictDetector? containerAceDetector = null,
        IReadOnlyList<TestGrantIntentStore>? loadedStores = null)
    {
        var databaseProvider = new LambdaDatabaseProvider(() => database);
        var pathInfo = new TestFileSystemPathInfo();
        var aclPermission = new Mock<IAclPermissionService>();
        var reparsePointHelper = new Mock<IReparsePointPromptHelper>();
        reparsePointHelper.Setup(r => r.ResolveForAdd(It.IsAny<string>(), It.IsAny<IWin32Window>()))
            .Returns<string, IWin32Window>((p, _) => [p]);

        var renderer = new AclManagerGrantRowRenderer(
            pathGrantService,
            new Mock<IAclPathIconProvider>().Object,
            new Mock<ILoggingService>().Object);
        using var grid = new DataGridView();
        renderer.Initialize(grid, sid, isContainer: false, groupSids: [], pending);

        var traversePathResolver = new GrantTraversePathResolver(pathInfo);
        var traverseAutoManager = new TraverseAutoManager(
            aclPermission.Object, databaseProvider, traversePathResolver, pathInfo, new TraverseGrantOwnerResolver());
        traverseAutoManager.Initialize(pending, sid, groupSids: []);

        var traverseOperations = new AclManagerTraverseOperations(
            databaseProvider, reparsePointHelper.Object, aclPermission.Object, pathInfo);
        traverseOperations.Initialize(sid, pending, new Lazy<IReadOnlyList<string>>(() => []), () => { });
        var grantIntentStoreProvider = new TestGrantIntentStoreProvider(new TestGrantIntentStore());
        foreach (var store in loadedStores ?? [])
            grantIntentStoreProvider.AddLoadedStore(store);
        var grantIntentRepository = new GrantIntentRepository(grantIntentStoreProvider);

        var grantsHelper = new AclManagerGrantsHelper(
            new Mock<IAppConfigService>().Object,
            grantIntentRepository,
            grantIntentStoreProvider,
            databaseProvider,
            new Mock<ISessionSaver>().Object,
            traverseAutoManager,
            new AclManagerPendingStateHelper(),
            () => renderer);
        grantsHelper.Initialize(grid, sid, isContainer: false, groupSids: [], pending);

        var orchestrator = new AclManagerActionOrchestrator(
            databaseProvider,
            grantsHelper,
            null!,
            traverseOperations,
            traverseAutoManager,
            reparsePointHelper.Object,
            containerAceDetector ?? new Mock<ISpecificContainerAceConflictDetector>().Object);
        orchestrator.Initialize(sid, isContainer: false, new Mock<IWin32Window>().Object, pending, refresher);
        return orchestrator;
    }

    private sealed class TestGridRefresher : IAclManagerGridRefresher
    {
        public int GrantsRefreshCount { get; private set; }

        public void RefreshGrantsGrid() => GrantsRefreshCount++;

        public void RefreshTraverseGrid()
        {
        }
    }
}
