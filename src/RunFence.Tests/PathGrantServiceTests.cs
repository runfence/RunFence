using System.Security.AccessControl;
using System.Security.Principal;
using Moq;
using RunFence.Acl;
using RunFence.Acl.Permissions;
using RunFence.Acl.Traverse;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Tests for <see cref="PathGrantService"/> covering grant operations, traverse management,
/// container interactive-user sync, bulk operations, and utility methods.
/// NTFS reads/writes are prevented by mocking <see cref="IAclAccessor"/> (wrapped in a real
/// <see cref="GrantNtfsHelper"/>), <see cref="ITraverseAcl"/>, and <see cref="IAclPermissionService"/>.
/// <see cref="AncestorTraverseGranter"/> is used directly (not mocked) with a no-op
/// <see cref="ITraverseAcl"/> mock so no ACEs are written.
/// </summary>
public class PathGrantServiceTests
{
    private const string UserSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
    private const string ContainerSid = "S-1-15-2-99-1-2-3-4-5-6";
    private const string OtherContainerSid = "S-1-15-2-99-1-2-3-4-5-7";
    private const string InteractiveSid = "S-1-5-21-1234567890-1234567890-1234567890-1002";
    private const string TestPath = @"C:\TestFolder\SubDir";

    private readonly Mock<IAclAccessor> _aclAccessor = new();
    private readonly Mock<ITraverseAcl> _traverseAcl = new();
    private readonly Mock<IAclPermissionService> _aclPermission = new();
    private readonly Mock<IInteractiveUserResolver> _iuResolver = new();
    private readonly Mock<ILoggingService> _log = new();

    private readonly AppDatabase _database = new();
    private readonly IPathGrantService _service;
    private readonly AncestorTraverseGranter _ancestorGranter;
    private readonly IGrantNtfsHelper _ntfsHelper;

    private static readonly IUiThreadInvoker SyncInvoker =
        new LambdaUiThreadInvoker(a => a(), a => a(), a => a());

    public PathGrantServiceTests()
    {
        // HasExplicitTraverseAce=true is used by PromoteNearestAncestor (to detect existing ACEs on
        // ancestor paths) and by the fallback path in AncestorTraverseGranter (when groupSids is null).
        _traverseAcl.Setup(t => t.HasExplicitTraverseAce(It.IsAny<string>(),
                It.IsAny<SecurityIdentifier>()))
            .Returns(true);

        // No group SIDs; NeedsPermissionGrant=true means grant is needed by default.
        _aclPermission.Setup(p => p.ResolveAccountGroupSids(It.IsAny<string>()))
            .Returns([]);
        _aclPermission.Setup(p => p.NeedsPermissionGrant(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<FileSystemRights>(), It.IsAny<bool>()))
            .Returns(true);
        // HasEffectiveRights=true signals "already covered" to AncestorTraverseGranter, so no
        // traverse ACEs are written to disk (AncestorTraverseGranter uses HasEffectiveRights when
        // groupSids is non-null, which is always the case since ResolveAccountGroupSids returns []).
        _aclPermission.Setup(p => p.HasEffectiveRights(
                It.IsAny<FileSystemSecurity>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<FileSystemRights>()))
            .Returns(true);

        _iuResolver.Setup(r => r.GetInteractiveUserSid()).Returns((string?)null);

        _ntfsHelper = new GrantNtfsHelper(_aclAccessor.Object, _log.Object);
        _ancestorGranter = new AncestorTraverseGranter(_log.Object, _aclPermission.Object,
            _traverseAcl.Object);

        _service = BuildService(_database);
    }

    private IPathGrantService BuildService(AppDatabase db)
    {
        var dbAccessor = new UiThreadDatabaseAccessor(new LambdaDatabaseProvider(() => db), SyncInvoker);
        var grantCore = new GrantCoreOperations(_ntfsHelper, dbAccessor, _log.Object);
        var traverseCore = new TraverseCoreOperations(_traverseAcl.Object,
            _ancestorGranter, _aclPermission.Object, dbAccessor, _log.Object);
        var containerIuSync = new ContainerInteractiveUserSync(grantCore, traverseCore,
            _iuResolver.Object, _aclPermission.Object, dbAccessor, _log.Object);
        var syncService = new PathGrantSyncService(dbAccessor, _ntfsHelper, _log.Object);
        return new PathGrantService(grantCore, traverseCore, _ntfsHelper,
            _iuResolver.Object, _aclPermission.Object, dbAccessor, containerIuSync, syncService);
    }

    private IPathGrantService BuildServiceWithIuResolver(AppDatabase db, string iuSid)
    {
        var iuResolver = new Mock<IInteractiveUserResolver>();
        iuResolver.Setup(r => r.GetInteractiveUserSid()).Returns(iuSid);

        var dbAccessor = new UiThreadDatabaseAccessor(new LambdaDatabaseProvider(() => db), SyncInvoker);
        var grantCore = new GrantCoreOperations(_ntfsHelper, dbAccessor, _log.Object);
        var traverseCore = new TraverseCoreOperations(_traverseAcl.Object,
            _ancestorGranter, _aclPermission.Object, dbAccessor, _log.Object);
        var containerIuSync = new ContainerInteractiveUserSync(grantCore, traverseCore,
            iuResolver.Object, _aclPermission.Object, dbAccessor, _log.Object);
        var syncService = new PathGrantSyncService(dbAccessor, _ntfsHelper, _log.Object);
        return new PathGrantService(grantCore, traverseCore, _ntfsHelper,
            iuResolver.Object, _aclPermission.Object, dbAccessor, containerIuSync, syncService);
    }

    /// <summary>
    /// Builds a <see cref="PathGrantService"/> backed by a mocked <see cref="IGrantNtfsHelper"/>
    /// so that <c>ChangeOwner</c>/<c>ResetOwner</c> calls can be verified without real NTFS I/O.
    /// </summary>
    private PathGrantService BuildServiceWithMockedNtfs(out Mock<IGrantNtfsHelper> ntfsMock, out AppDatabase db)
    {
        ntfsMock = new Mock<IGrantNtfsHelper>();
        var localDb = new AppDatabase();
        db = localDb;
        var dbAccessor = new UiThreadDatabaseAccessor(new LambdaDatabaseProvider(() => localDb), SyncInvoker);
        var grantCore = new GrantCoreOperations(ntfsMock.Object, dbAccessor, _log.Object);
        var traverseCore = new TraverseCoreOperations(_traverseAcl.Object,
            _ancestorGranter, _aclPermission.Object, dbAccessor, _log.Object);
        var containerIuSync = new ContainerInteractiveUserSync(grantCore, traverseCore,
            _iuResolver.Object, _aclPermission.Object, dbAccessor, _log.Object);
        var syncService = new PathGrantSyncService(dbAccessor, ntfsMock.Object, _log.Object);
        return new PathGrantService(grantCore, traverseCore, ntfsMock.Object,
            _iuResolver.Object, _aclPermission.Object, dbAccessor, containerIuSync, syncService);
    }

    private static SavedRightsState ReadOnly =>
        new(Execute: false, Write: false, Read: true, Special: false, Own: false);

    private static SavedRightsState ReadExecute =>
        new(Execute: true, Write: false, Read: true, Special: false, Own: false);

    private static SavedRightsState DefaultDeny =>
        SavedRightsState.DefaultForMode(isDeny: true);

    // --- AddGrant ---

    [Fact]
    public void AddGrant_NewEntry_AddsToDbAndReturnsGrantAdded()
    {
        // Act
        var result = _service.AddGrant(UserSid, TestPath, isDeny: false, ReadOnly);

        // Assert
        Assert.True(result.GrantAdded);
        Assert.True(result.DatabaseModified);
        var grants = _database.GetAccount(UserSid)?.Grants;
        Assert.NotNull(grants);
        Assert.Contains(grants, e => !e.IsTraverseOnly && !e.IsDeny && e.Path == TestPath);
    }

    [Fact]
    public void AddGrant_DuplicateSameMode_UpdatesSavedRightsInPlace()
    {
        // Arrange
        _service.AddGrant(UserSid, TestPath, isDeny: false, ReadOnly);

        // Act — add again with different rights (same mode, same path)
        var result = _service.AddGrant(UserSid, TestPath, isDeny: false, ReadExecute);

        // Assert — no new non-traverse entry, existing entry updated
        Assert.False(result.GrantAdded);
        Assert.True(result.DatabaseModified);
        var grants = _database.GetAccount(UserSid)!.Grants
            .Where(e => !e.IsTraverseOnly && !e.IsDeny).ToList();
        Assert.Single(grants);
        Assert.True(grants[0].SavedRights!.Execute);
    }

    [Fact]
    public void AddGrant_OppositeModeExists_Throws()
    {
        // Arrange: add allow grant first
        _service.AddGrant(UserSid, TestPath, isDeny: false, ReadOnly);

        // Act + Assert: adding deny for same path throws
        Assert.Throws<InvalidOperationException>(
            () => _service.AddGrant(UserSid, TestPath, isDeny: true, DefaultDeny));
    }

    [Fact]
    public void AddGrant_NullSavedRights_UsesDefaultForMode()
    {
        // Act
        _service.AddGrant(UserSid, TestPath, isDeny: false, savedRights: null);

        // Assert — entry recorded with DefaultForMode rights
        var entry = _database.GetAccount(UserSid)!.Grants
            .First(e => !e.IsTraverseOnly && !e.IsDeny);
        Assert.NotNull(entry.SavedRights);
        Assert.True(entry.SavedRights.Read);
    }

    [Fact]
    public void AddGrant_AllowGrant_AutoAddsTraverseEntry()
    {
        // Act
        _service.AddGrant(UserSid, TestPath, isDeny: false, ReadOnly);

        // Assert — a traverse entry for the same directory was added
        var traverseEntries = _database.GetAccount(UserSid)?.Grants
            .Where(e => e.IsTraverseOnly).ToList();
        Assert.NotNull(traverseEntries);
        Assert.NotEmpty(traverseEntries);
    }

    [Fact]
    public void AddGrant_ContainerSid_TriggersInteractiveUserSync()
    {
        // Arrange: IU resolver returns a valid SID; IU needs the grant
        var db = new AppDatabase();
        var service = BuildServiceWithIuResolver(db, InteractiveSid);

        // Act
        service.AddGrant(ContainerSid, TestPath, isDeny: false, ReadOnly);

        // Assert — IU also has grant
        var iuGrants = db.GetAccount(InteractiveSid)?.Grants;
        Assert.NotNull(iuGrants);
        Assert.Contains(iuGrants, e => !e.IsTraverseOnly && !e.IsDeny && e.Path == TestPath);
    }

    [Fact]
    public void AddGrant_ContainerSid_IuGrantSkippedWhenRightsAlreadySufficient()
    {
        // Arrange: IU already has sufficient rights (NeedsPermissionGrant = false for IU)
        _aclPermission.Setup(p => p.NeedsPermissionGrant(TestPath, InteractiveSid,
                It.IsAny<FileSystemRights>(), It.IsAny<bool>()))
            .Returns(false);

        var db = new AppDatabase();
        var service = BuildServiceWithIuResolver(db, InteractiveSid);

        // Act
        service.AddGrant(ContainerSid, TestPath, isDeny: false, ReadOnly);

        // Assert — IU has no non-traverse grant for TestPath
        var iuNonTraverse = db.GetAccount(InteractiveSid)?.Grants
            .Where(e => !e.IsTraverseOnly && !e.IsDeny && e.Path == TestPath).ToList();
        Assert.Empty(iuNonTraverse ?? []);
    }

    [Fact]
    public void AddGrant_WithOwnerSid_RecordsGrantAndAppliesAce()
    {
        // Arrange: use a real directory (temp) so SetOwnerInternal does not throw.
        // Set owner to the current process identity — the test user owns the temp dir
        // and can set owner back to themselves without elevation.
        var tempDir = Path.GetFullPath(Path.GetTempPath());
        var currentUserSid = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
        var db = new AppDatabase();
        var service = BuildService(db);

        // Act
        var result = service.AddGrant(UserSid, tempDir, isDeny: false, ReadOnly, ownerSid: currentUserSid);

        // Assert: grant recorded; ACE applied; SetOwner called (no exception = success)
        Assert.True(result.GrantAdded);
        Assert.True(result.DatabaseModified);
        var entry = db.GetAccount(UserSid)?.Grants
            .FirstOrDefault(e => !e.IsTraverseOnly && !e.IsDeny &&
                string.Equals(e.Path, tempDir, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(entry);
        _aclAccessor.Verify(a => a.ApplyExplicitAce(
            tempDir, UserSid, AccessControlType.Allow, It.IsAny<FileSystemRights>()), Times.Once);
    }

    // --- UpdateGrant ---

    [Fact]
    public void UpdateGrant_ChangesRightsOnExistingEntry()
    {
        // Arrange: add a grant then update it with different rights
        _service.AddGrant(UserSid, TestPath, isDeny: false, ReadOnly);

        // Act
        _service.UpdateGrant(UserSid, TestPath, isDeny: false, ReadExecute);

        // Assert: DB entry has new SavedRights
        var entry = _database.GetAccount(UserSid)!.Grants
            .First(e => !e.IsTraverseOnly && !e.IsDeny);
        Assert.NotNull(entry.SavedRights);
        Assert.True(entry.SavedRights!.Execute);
    }

    [Fact]
    public void UpdateGrant_AppliesNtfsAce()
    {
        // Arrange
        _service.AddGrant(UserSid, TestPath, isDeny: false, ReadOnly);

        // Reset the mock call count after AddGrant
        _aclAccessor.Invocations.Clear();

        // Act
        _service.UpdateGrant(UserSid, TestPath, isDeny: false, ReadExecute);

        // Assert: NTFS ACE re-applied exactly once
        _aclAccessor.Verify(a => a.ApplyExplicitAce(
            It.IsAny<string>(), UserSid, AccessControlType.Allow, It.IsAny<FileSystemRights>()),
            Times.Once);
    }

    [Fact]
    public void UpdateGrant_WithOwnerSid_CallsChangeOwnerWithCorrectSid()
    {
        // Arrange: mock IGrantNtfsHelper directly so ChangeOwner can be verified without real NTFS calls.
        const string ownerSid = "S-1-5-21-9999-9999-9999-1001";
        var service = BuildServiceWithMockedNtfs(out var ntfsMock, out var db);

        // Pre-populate DB entry directly so no NTFS calls from AddGrant interfere with verification.
        db.GetOrCreateAccount(UserSid).Grants.Add(
            new GrantedPathEntry { Path = TestPath, IsDeny = false, SavedRights = ReadOnly });

        // Act
        service.UpdateGrant(UserSid, TestPath, isDeny: false, ReadExecute, ownerSid: ownerSid);

        // Assert: ChangeOwner called with the correct owner SID (non-recursive for UpdateGrant)
        ntfsMock.Verify(n => n.ChangeOwner(It.IsAny<string>(), ownerSid, false), Times.Once);
    }

    [Fact]
    public void UpdateGrant_ModeUnchanged_NoNewEntryAdded()
    {
        // Arrange: single allow grant
        _service.AddGrant(UserSid, TestPath, isDeny: false, ReadOnly);

        // Act: update (same mode, same path)
        _service.UpdateGrant(UserSid, TestPath, isDeny: false, ReadExecute);

        // Assert: still only one non-traverse allow entry
        var grants = _database.GetAccount(UserSid)!.Grants
            .Where(e => !e.IsTraverseOnly && !e.IsDeny).ToList();
        Assert.Single(grants);
    }

    // --- FixGrant ---

    [Fact]
    public void FixGrant_ExistingEntry_ReappliesNtfsAceWithoutDbChange()
    {
        // Arrange: pre-populate the DB entry directly (bypassing NTFS)
        _database.GetOrCreateAccount(UserSid).Grants.Add(
            new GrantedPathEntry { Path = TestPath, IsDeny = false, SavedRights = ReadOnly });

        // Act
        _service.FixGrant(UserSid, TestPath, isDeny: false);

        // Assert: NTFS ACE applied once (no prior AddGrant calls — DB was populated directly)
        _aclAccessor.Verify(a => a.ApplyExplicitAce(
            It.IsAny<string>(), UserSid, AccessControlType.Allow, It.IsAny<FileSystemRights>()),
            Times.Once);

        // DB entry unchanged (still has SavedRights = ReadOnly; FixGrant does not modify SavedRights)
        var entry = _database.GetAccount(UserSid)!.Grants
            .First(e => !e.IsTraverseOnly && !e.IsDeny);
        Assert.NotNull(entry.SavedRights);
        Assert.False(entry.SavedRights!.Execute);
    }

    [Fact]
    public void FixGrant_NoEntryInDb_DoesNotApplyAce()
    {
        // No DB entry for TestPath → FixGrant is a no-op
        _service.FixGrant(UserSid, TestPath, isDeny: false);

        _aclAccessor.Verify(a => a.ApplyExplicitAce(
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<AccessControlType>(), It.IsAny<FileSystemRights>()),
            Times.Never);
    }

    [Fact]
    public void FixGrant_NullSavedRights_UsesDefaultForMode()
    {
        // Arrange: DB entry with null SavedRights (legacy entry)
        _database.GetOrCreateAccount(UserSid).Grants.Add(
            new GrantedPathEntry { Path = TestPath, IsDeny = false, SavedRights = null });

        // Act: FixGrant should use DefaultForMode (Read=true) rather than throwing
        _service.FixGrant(UserSid, TestPath, isDeny: false);

        // Assert: ACE applied once using default rights
        _aclAccessor.Verify(a => a.ApplyExplicitAce(
            It.IsAny<string>(), UserSid, AccessControlType.Allow, It.IsAny<FileSystemRights>()),
            Times.Once);
    }

    // --- FixTraverse ---

    [Fact]
    public void FixTraverse_ExistingEntry_DoesNotDuplicateDbEntry()
    {
        // Arrange: pre-populate traverse entry for TestPath
        _database.GetOrCreateAccount(UserSid).Grants.Add(
            new GrantedPathEntry { Path = TestPath, IsTraverseOnly = true });

        // Act
        _service.FixTraverse(UserSid, TestPath);

        // Assert: no duplicate traverse entry added (FixTraverse updates AllAppliedPaths on the existing
        // entry but does not insert a new one)
        var traverseEntries = _database.GetAccount(UserSid)!.Grants
            .Where(e => e.IsTraverseOnly && e.Path == TestPath).ToList();
        Assert.Single(traverseEntries); // still exactly one
    }

    [Fact]
    public void FixTraverse_ReturnsVisitedPaths()
    {
        // Arrange: use a path that exists so AncestorTraverseGranter visits at least one ancestor
        var tempDir = Path.GetFullPath(Path.GetTempPath());
        _database.GetOrCreateAccount(UserSid).Grants.Add(
            new GrantedPathEntry { Path = tempDir, IsTraverseOnly = true });

        // Act
        var visited = _service.FixTraverse(UserSid, tempDir);

        // Assert: at least one ancestor path visited
        Assert.NotEmpty(visited);
    }

    // --- EnsureAccess ---

    [Fact]
    public void EnsureAccess_AlreadySufficientWithDbEntry_DoesNotApplyAce()
    {
        // Arrange: existing DB entry + disk ACE matching DB + effective rights sufficient.
        // A real existing directory is used so pathExists=true and the auto-fix check runs.
        // The security descriptor has an explicit ReadMask ACE matching the DB SavedRights,
        // so needsFix=false. Combined with NeedsPermissionGrant=false, EnsureAccess returns no-op.
        var tempDir = Path.GetFullPath(Path.GetTempPath());

        // Blank security (no inherited ACEs) with exactly one explicit allow ACE matching ReadOnly.
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(UserSid), GrantRightsMapper.ReadMask,
            InheritanceFlags.None, PropagationFlags.None, AccessControlType.Allow));
        _aclAccessor.Setup(a => a.GetSecurity(tempDir)).Returns(security);

        // Pre-populate DB entry with matching SavedRights
        _database.GetOrCreateAccount(UserSid).Grants.Add(
            new GrantedPathEntry { Path = tempDir, IsDeny = false, SavedRights = ReadOnly });

        // Effective rights already sufficient
        _aclPermission.Setup(p => p.NeedsPermissionGrant(tempDir, UserSid,
                It.IsAny<FileSystemRights>(), It.IsAny<bool>()))
            .Returns(false);

        // Act
        var result = _service.EnsureAccess(UserSid, tempDir, ReadOnly);

        // Assert: no ACE applied (access was already sufficient and disk state matches DB)
        _aclAccessor.Verify(a => a.ApplyExplicitAce(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<AccessControlType>(), It.IsAny<FileSystemRights>()), Times.Never);
        Assert.False(result.GrantAdded);
    }

    [Fact]
    public void EnsureAccess_AlreadySufficientNoDbEntry_DoesNotPromptOrGrant()
    {
        // Arrange: no DB entry, path exists, but account already has sufficient rights.
        // This is the bug scenario: toolbar folder browser on user's own profile folder.
        var tempDir = Path.GetFullPath(Path.GetTempPath());
        _aclPermission.Setup(p => p.NeedsPermissionGrant(tempDir, UserSid,
                It.IsAny<FileSystemRights>(), It.IsAny<bool>()))
            .Returns(false);

        bool confirmCalled = false;

        // Act
        var result = _service.EnsureAccess(UserSid, tempDir, ReadOnly,
            confirm: (_, _) => { confirmCalled = true; return true; });

        // Assert: no prompt, no grant
        Assert.False(confirmCalled);
        Assert.False(result.GrantAdded);
        Assert.False(result.DatabaseModified);
    }

    [Fact]
    public void EnsureAccess_NewGrantNeeded_NullConfirm_ProceedsSilently()
    {
        // Arrange: no existing grant; path exists but account lacks access — grantNeeded=true via NeedsPermissionGrant.
        // Null confirm = silent grant, no prompt. Post-grant verification returns false (access now sufficient).
        var tempDir = Path.GetFullPath(Path.GetTempPath());
        _aclPermission.SetupSequence(p => p.NeedsPermissionGrant(tempDir, UserSid,
                It.IsAny<FileSystemRights>(), It.IsAny<bool>()))
            .Returns(true)   // grantNeeded check
            .Returns(false); // post-grant verification

        // Act
        var result = _service.EnsureAccess(UserSid, tempDir, ReadOnly, confirm: null);

        // Assert
        Assert.True(result.GrantAdded);
        var grants = _database.GetAccount(UserSid)?.Grants;
        Assert.Contains(grants!, e => !e.IsTraverseOnly && !e.IsDeny && e.Path == tempDir);
    }

    [Fact]
    public void EnsureAccess_NewGrantNeeded_ConfirmApproves_AppliesGrant()
    {
        // Arrange: no existing grant; path exists but account lacks access.
        // confirm is called before applying the grant; post-grant verification returns false (access now sufficient).
        var tempDir = Path.GetFullPath(Path.GetTempPath());
        _aclPermission.SetupSequence(p => p.NeedsPermissionGrant(tempDir, UserSid,
                It.IsAny<FileSystemRights>(), It.IsAny<bool>()))
            .Returns(true)   // grantNeeded check
            .Returns(false); // post-grant verification

        bool confirmCalled = false;

        // Act
        var result = _service.EnsureAccess(UserSid, tempDir, ReadOnly,
            confirm: (_, _) => { confirmCalled = true; return true; });

        // Assert
        Assert.True(confirmCalled);
        Assert.True(result.GrantAdded);
    }

    [Fact]
    public void EnsureAccess_ConfirmRejectsGrant_ThrowsOperationCanceledException()
    {
        // Arrange: no existing grant; path exists but account lacks access.
        // NeedsPermissionGrant=true (default) → grantNeeded=true → confirm called → rejects → OCE.
        var tempDir = Path.GetFullPath(Path.GetTempPath());

        // Act + Assert: confirm rejects the grant → OperationCanceledException
        Assert.Throws<OperationCanceledException>(
            () => _service.EnsureAccess(UserSid, tempDir, ReadOnly,
                confirm: (_, _) => false));
    }

    [Fact]
    public void EnsureAccess_NonExistentPath_NoDbEntry_ReturnsNoOp()
    {
        // Arrange: path does not exist, no DB entry — cannot check or modify NTFS permissions.
        // Expected: no prompt, no grant, no-op (silent return regardless of confirm).
        bool confirmCalled = false;

        // Act
        var result = _service.EnsureAccess(UserSid, TestPath, ReadOnly,
            confirm: (_, _) => { confirmCalled = true; return true; });

        // Assert
        Assert.False(confirmCalled);
        Assert.False(result.GrantAdded);
        Assert.False(result.DatabaseModified);
    }

    [Fact]
    public void EnsureAccess_MergesFlags_NeverReducesExistingAccess()
    {
        // Arrange: existing grant has Execute=true; disk ACE is missing (needsFix=true) so EnsureAccess
        // will re-apply the grant using merged rights (ReadOnly requested but Execute must be preserved).
        var tempDir = Path.GetFullPath(Path.GetTempPath());

        // Empty ACL → DirectAllowAceCount=0 → needsFix=true → EnsureAccess applies merged rights
        _aclAccessor.Setup(a => a.GetSecurity(tempDir)).Returns(EmptySecurity());
        // needsFix short-circuits NeedsPermissionGrant for grantNeeded; one call for post-verification
        _aclPermission.Setup(p => p.NeedsPermissionGrant(tempDir, UserSid,
                It.IsAny<FileSystemRights>(), It.IsAny<bool>()))
            .Returns(false);

        _service.AddGrant(UserSid, tempDir, isDeny: false, ReadExecute);

        // Act: request ReadOnly (no Execute flag)
        _service.EnsureAccess(UserSid, tempDir, ReadOnly, confirm: null);

        // Assert: grant still has Execute=true (merge preserved it, not reduced)
        var entry = _database.GetAccount(UserSid)!.Grants
            .First(e => !e.IsTraverseOnly && !e.IsDeny && e.Path == tempDir);
        Assert.True(entry.SavedRights!.Execute);
    }

    [Fact]
    public void EnsureAccess_AutoFix_DiskAceMissing_ReappliesAce()
    {
        // Arrange: DB entry exists but disk has no explicit allow ACE (DirectAllowAceCount == 0).
        // This simulates a state where the ACE was lost but the DB still records the grant.
        var tempDir = Path.GetFullPath(Path.GetTempPath());

        // Empty security (no explicit ACEs) so DirectAllowAceCount = 0 → needsFix=true
        var emptyAcl = EmptySecurity();
        _aclAccessor.Setup(a => a.GetSecurity(tempDir)).Returns(emptyAcl);

        // Pre-populate the DB entry
        _database.GetOrCreateAccount(UserSid).Grants.Add(
            new GrantedPathEntry { Path = tempDir, IsDeny = false, SavedRights = ReadOnly });

        // needsFix=true short-circuits grantNeeded without calling NeedsPermissionGrant, so only one call
        // is made: the post-grant check. It must return false so the check passes.
        _aclPermission.Setup(p => p.NeedsPermissionGrant(tempDir, UserSid,
                It.IsAny<FileSystemRights>(), It.IsAny<bool>()))
            .Returns(false);

        // Act: null confirm — auto-fix applies grant silently
        var result = _service.EnsureAccess(UserSid, tempDir, ReadOnly, confirm: null);

        // Assert: ACE was re-applied (AddGrant called ApplyExplicitAce)
        Assert.True(result.DatabaseModified);
        _aclAccessor.Verify(a => a.ApplyExplicitAce(tempDir, UserSid,
            AccessControlType.Allow, It.IsAny<FileSystemRights>()), Times.AtLeastOnce);
    }

    [Fact]
    public void EnsureAccess_AutoFix_SkippedWhenPathDoesNotExist()
    {
        // Arrange: DB entry exists for a non-existent path.
        // The auto-fix check (which reads disk ACL via acl.GetSecurity) is gated on pathExists
        // and must be skipped entirely for non-existent paths. Since no NTFS check is possible,
        // EnsureAccess returns no-op rather than attempting a grant that would fail.
        const string nonExistentPath = @"C:\DoesNotExistNever\subdir";

        _database.GetOrCreateAccount(UserSid).Grants.Add(
            new GrantedPathEntry { Path = nonExistentPath, IsDeny = false, SavedRights = ReadOnly });

        // Act
        var result = _service.EnsureAccess(UserSid, nonExistentPath, ReadOnly, confirm: null);

        // Assert: disk ACL was NOT read (auto-fix check is gated on pathExists)
        _aclAccessor.Verify(a => a.GetSecurity(It.IsAny<string>()), Times.Never);
        // No grant was attempted (pathExists=false → NeedsPermissionGrant not called → no-op)
        Assert.False(result.GrantAdded);
        Assert.False(result.DatabaseModified);
    }

    [Fact]
    public void EnsureAccess_DenyConflictNullConfirm_ThrowsInvalidOperationException()
    {
        // Arrange: existing deny entry for same path + sid (deny with Read=true)
        var denyRights = new SavedRightsState(Execute: false, Write: true, Read: true, Special: true, Own: false);
        _service.AddGrant(UserSid, TestPath, isDeny: true, denyRights);

        // Act + Assert
        Assert.Throws<InvalidOperationException>(
            () => _service.EnsureAccess(UserSid, TestPath, ReadOnly, confirm: null));
    }

    [Fact]
    public void EnsureAccess_DenyConflictConfirmApproved_WeakensOrRemovesDenyEntry()
    {
        // Arrange: deny with Read=true
        var denyRights = new SavedRightsState(Execute: false, Write: true, Read: true, Special: true, Own: false);
        _service.AddGrant(UserSid, TestPath, isDeny: true, denyRights);

        // Act: request allow with Read=true — deny Read should be resolved
        _service.EnsureAccess(UserSid, TestPath, ReadOnly,
            confirm: (_, _) => true);

        // Assert: deny Read flag is now cleared (or deny fully removed if no conflicting flags remain).
        // Requesting Read-only allow against a deny with Read=true, Execute=false:
        // newDenyRead = existingDeny.Read && !requestedAllow.Read = true && false = false
        // newDenyExecute = existingDeny.Execute && !requestedAllow.Execute = false && true = false
        // → deny fully removed.
        var denyEntry = _database.GetAccount(UserSid)?.Grants
            .FirstOrDefault(e => !e.IsTraverseOnly && e.IsDeny && e.Path == TestPath);
        Assert.Null(denyEntry); // fully removed: no remaining conflicting deny flags
    }

    [Fact]
    public void EnsureAccess_DenyConflictConfirmRejected_ThrowsOperationCanceledException()
    {
        // Arrange: existing deny with Read=true
        var denyRights = new SavedRightsState(Execute: false, Write: true, Read: true, Special: true, Own: false);
        _service.AddGrant(UserSid, TestPath, isDeny: true, denyRights);

        // Act + Assert
        Assert.Throws<OperationCanceledException>(
            () => _service.EnsureAccess(UserSid, TestPath, ReadOnly,
                confirm: (_, _) => false));
    }

    [Fact]
    public void EnsureAccess_PostGrantVerificationFails_ThrowsInvalidOperationException()
    {
        // Arrange: use a real existing directory so pathExists=true and the post-grant
        // verification runs. NeedsPermissionGrant always returns true (default mock),
        // simulating a parent deny that blocks access even after the grant is applied.
        var tempDir = Path.GetTempPath();

        // Act + Assert
        Assert.Throws<InvalidOperationException>(
            () => _service.EnsureAccess(UserSid, tempDir, ReadOnly, confirm: null));
    }

    [Fact]
    public void EnsureAccess_FileSystemRightsOverload_DelegatesToSavedRightsOverload()
    {
        // Arrange: path exists, account lacks access → grant applied. Uses SetupSequence so
        // post-grant verification returns false (access now sufficient after the grant).
        var tempDir = Path.GetFullPath(Path.GetTempPath());
        _aclPermission.SetupSequence(p => p.NeedsPermissionGrant(tempDir, UserSid,
                It.IsAny<FileSystemRights>(), It.IsAny<bool>()))
            .Returns(true)   // grantNeeded check
            .Returns(false); // post-grant verification

        // Act — use the FileSystemRights overload
        var result = _service.EnsureAccess(UserSid, tempDir,
            FileSystemRights.ReadAndExecute, confirm: null);

        // Assert
        Assert.True(result.GrantAdded);
    }

    [Fact]
    public void EnsureAccess_AutoFix_MissingTraverseAce_ReappliesTraverseOnly()
    {
        // Arrange: DB has a correct grant entry AND a traverse entry for tempDir, but the
        // traverse ACE is missing on disk (HasEffectiveTraverse=false). The grant ACE is correct
        // (DirectAllowAceCount=1, matching diskRights). NeedsPermissionGrant returns false
        // (file rights are fine). Only the traverse needs to be re-applied.
        var tempDir = Path.GetFullPath(Path.GetTempPath());

        // Security with correct allow ACE so needsFix=false (disk ACE matches)
        var security = CreateSecurityWithAllowAce(UserSid, GrantRightsMapper.ReadMask);
        _aclAccessor.Setup(a => a.GetSecurity(tempDir)).Returns(security);

        // Pre-populate grant and traverse entries
        _database.GetOrCreateAccount(UserSid).Grants.Add(
            new GrantedPathEntry { Path = tempDir, IsDeny = false, SavedRights = ReadOnly });
        _database.GetOrCreateAccount(UserSid).Grants.Add(
            new GrantedPathEntry { Path = tempDir, IsTraverseOnly = true });

        // Rights are sufficient — only traverse ACE is missing
        _aclPermission.Setup(p => p.NeedsPermissionGrant(tempDir, UserSid,
                It.IsAny<FileSystemRights>(), It.IsAny<bool>()))
            .Returns(false);

        // HasEffectiveTraverse calls HasEffectiveRights; override to return false so traverseNeedsFix=true
        _aclPermission.Setup(p => p.HasEffectiveRights(
                It.IsAny<FileSystemSecurity>(), UserSid,
                It.IsAny<IReadOnlyList<string>>(), TraverseRightsHelper.TraverseRights))
            .Returns(false);

        // Act
        var result = _service.EnsureAccess(UserSid, tempDir, ReadOnly, confirm: null);

        // Assert: traverse was re-applied, grant was not re-added
        Assert.False(result.GrantAdded);
        Assert.True(result.TraverseAdded);
    }

    // --- RemoveGrant ---

    [Fact]
    public void RemoveGrant_ExistingEntry_RemovesFromDbAndCallsRevert()
    {
        // Arrange
        _service.AddGrant(UserSid, TestPath, isDeny: false, ReadOnly);

        // Act
        var removed = _service.RemoveGrant(UserSid, TestPath, isDeny: false, updateFileSystem: true);

        // Assert
        Assert.True(removed);
        var grants = _database.GetAccount(UserSid)?.Grants
            .Where(e => !e.IsTraverseOnly && !e.IsDeny).ToList();
        Assert.Empty(grants ?? []);
        _aclAccessor.Verify(a => a.RemoveExplicitAces(TestPath, UserSid,
            AccessControlType.Allow), Times.AtLeastOnce);
    }

    [Fact]
    public void RemoveGrant_UpdateFsFalse_SkipsNtfsRevert()
    {
        // Arrange
        _service.AddGrant(UserSid, TestPath, isDeny: false, ReadOnly);

        // Act
        _service.RemoveGrant(UserSid, TestPath, isDeny: false, updateFileSystem: false);

        // Assert — RemoveExplicitAces NOT called
        _aclAccessor.Verify(a => a.RemoveExplicitAces(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<AccessControlType>()), Times.Never);
    }

    [Fact]
    public void RemoveGrant_NonExistentEntry_ReturnsFalse()
    {
        var removed = _service.RemoveGrant(UserSid, TestPath, isDeny: false, updateFileSystem: false);

        Assert.False(removed);
    }

    [Fact]
    public void RemoveGrant_OrphanedTraverseCleaned_WhenNoOtherGrantsNeedIt()
    {
        // Arrange: add grant for a real directory so the traverse entry's path actually exists.
        // This ensures pathIsStale=false in RemoveTraverse and PromoteNearestAncestor does not run.
        var tempDir = Path.GetFullPath(Path.GetTempPath());
        _service.AddGrant(UserSid, tempDir, isDeny: false, ReadOnly);

        // Verify traverse was auto-added
        var traverseBefore = _database.GetAccount(UserSid)?.Grants
            .Where(e => e.IsTraverseOnly).ToList();
        Assert.NotEmpty(traverseBefore ?? []);

        // Act: remove the grant — traverse for tempDir should be cleaned up
        _service.RemoveGrant(UserSid, tempDir, isDeny: false, updateFileSystem: true);

        // Assert: no traverse entries remain for tempDir (the only grant path)
        var traverseForTemp = _database.GetAccount(UserSid)?.Grants
            .Where(e => e.IsTraverseOnly &&
                        string.Equals(e.Path, tempDir, StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Empty(traverseForTemp ?? []);
    }

    [Fact]
    public void RemoveGrant_TraversePreservedWhenOtherGrantNeedsIt()
    {
        // Arrange: add two grants for different paths that share the same parent directory.
        // Both grants auto-add traverse for the same parent dir.
        // Removing one grant must not clean up the traverse because the other still needs it.
        const string path1 = @"C:\TestFolder\File1.exe";
        const string path2 = @"C:\TestFolder\File2.exe";

        _service.AddGrant(UserSid, path1, isDeny: false, ReadOnly);
        _service.AddGrant(UserSid, path2, isDeny: false, ReadOnly);

        var traverseBefore = _database.GetAccount(UserSid)?.Grants
            .Where(e => e.IsTraverseOnly).ToList();
        Assert.NotEmpty(traverseBefore ?? []);

        // Act: remove one grant — the other still needs the traverse
        _service.RemoveGrant(UserSid, path1, isDeny: false, updateFileSystem: true);

        // Assert: traverse entry still present (needed by path2 grant)
        var traverseAfter = _database.GetAccount(UserSid)?.Grants
            .Where(e => e.IsTraverseOnly).ToList();
        Assert.NotEmpty(traverseAfter ?? []);
    }

    [Fact]
    public void RemoveGrant_ContainerSid_RevertsMatchingInteractiveUserGrant()
    {
        // Arrange: container and IU both have grant for same path
        var db = new AppDatabase();
        var service = BuildServiceWithIuResolver(db, InteractiveSid);

        service.AddGrant(ContainerSid, TestPath, isDeny: false, ReadOnly);

        // Verify IU has the grant
        Assert.Contains(db.GetAccount(InteractiveSid)?.Grants ?? [],
            e => !e.IsTraverseOnly && !e.IsDeny && e.Path == TestPath);

        // Act: remove container grant
        service.RemoveGrant(ContainerSid, TestPath, isDeny: false, updateFileSystem: true);

        // Assert: IU grant also removed
        var iuEntry = db.GetAccount(InteractiveSid)?.Grants
            .FirstOrDefault(e => !e.IsTraverseOnly && !e.IsDeny && e.Path == TestPath);
        Assert.Null(iuEntry);
    }

    // --- AddTraverse / RemoveTraverse ---

    [Fact]
    public void AddTraverse_NewEntry_RecordsDbEntry()
    {
        // Act
        var (modified, visited) = _service.AddTraverse(UserSid, TestPath);

        // Assert — DB entry recorded. TestPath itself does not exist, so AncestorTraverseGranter
        // skips it and visits the nearest existing ancestor (C:\). anyAceAdded=false because
        // HasEffectiveRights returns true (default mock), but dbEntryIsNew=true so modified=true.
        var traverseEntry = _database.GetAccount(UserSid)?.Grants
            .FirstOrDefault(e => e.IsTraverseOnly && e.Path == TestPath);
        Assert.NotNull(traverseEntry);
        // Visited ancestor paths returned for cleanup tracking
        Assert.NotEmpty(visited);
    }

    [Fact]
    public void AddTraverse_ContainerSid_AlsoAddsTraverseForInteractiveUser()
    {
        // Arrange
        var db = new AppDatabase();
        var service = BuildServiceWithIuResolver(db, InteractiveSid);

        // Act
        service.AddTraverse(ContainerSid, TestPath);

        // Assert: IU also has a traverse entry
        var iuEntry = db.GetAccount(InteractiveSid)?.Grants
            .FirstOrDefault(e => e.IsTraverseOnly && e.Path == TestPath);
        Assert.NotNull(iuEntry);
    }

    [Fact]
    public void RemoveTraverse_ExistingEntry_RemovesFromDb()
    {
        // Arrange: add traverse first
        _service.AddTraverse(UserSid, TestPath);

        // Act
        var removed = _service.RemoveTraverse(UserSid, TestPath, updateFileSystem: true);

        // Assert
        Assert.True(removed);
        var traverseEntries = _database.GetAccount(UserSid)?.Grants
            .Where(e => e.IsTraverseOnly && e.Path == TestPath).ToList();
        Assert.Empty(traverseEntries ?? []);
    }

    [Fact]
    public void RemoveTraverse_NonExistentEntry_ReturnsFalse()
    {
        var removed = _service.RemoveTraverse(UserSid, TestPath, updateFileSystem: false);

        Assert.False(removed);
    }

    [Fact]
    public void RemoveTraverse_ContainerSid_RevertsInteractiveUserTraverse()
    {
        // Arrange: container and IU both have traverse
        var db = new AppDatabase();
        var service = BuildServiceWithIuResolver(db, InteractiveSid);

        service.AddTraverse(ContainerSid, TestPath);

        // Verify IU has traverse
        Assert.Contains(db.GetAccount(InteractiveSid)?.Grants ?? [],
            e => e.IsTraverseOnly && e.Path == TestPath);

        // Act
        service.RemoveTraverse(ContainerSid, TestPath, updateFileSystem: true);

        // Assert: IU traverse removed
        var iuEntry = db.GetAccount(InteractiveSid)?.Grants
            .FirstOrDefault(e => e.IsTraverseOnly && e.Path == TestPath);
        Assert.Null(iuEntry);
    }

    [Fact]
    public void RemoveTraverse_PromotesNearestAncestor_WhenTargetPathIsStale()
    {
        // Arrange: manually insert a traverse entry for a non-existent path with a list of
        // applied ancestor paths. One of those ancestors exists on disk and has an explicit
        // traverse ACE → it should be promoted to a standalone DB entry.
        const string stalePath = @"C:\DoesNotExistNever\GoneDir";
        var tempDir = Path.GetFullPath(Path.GetTempPath()); // real path that "exists"

        // The stale entry references tempDir as an applied ancestor
        var staleEntry = new GrantedPathEntry
        {
            Path = stalePath,
            IsTraverseOnly = true,
            AllAppliedPaths = [tempDir]
        };
        _database.GetOrCreateAccount(UserSid).Grants.Add(staleEntry);

        // traverseAcl reports that tempDir has an explicit traverse ACE for the SID
        _traverseAcl.Setup(t => t.HasExplicitTraverseAce(
                tempDir, It.Is<SecurityIdentifier>(s => s.Value == UserSid)))
            .Returns(true);

        // Act: remove the stale traverse entry
        var removed = _service.RemoveTraverse(UserSid, stalePath, updateFileSystem: false);

        // Assert: stale entry removed; tempDir promoted as new standalone traverse entry
        Assert.True(removed);
        var grants = _database.GetAccount(UserSid)?.Grants;
        Assert.DoesNotContain(grants ?? [], e => e.IsTraverseOnly && e.Path == stalePath);
        Assert.Contains(grants ?? [], e => e.IsTraverseOnly &&
            string.Equals(e.Path, tempDir, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RemoveTraverse_PreservesAce_WhenGrantNeedsIt()
    {
        // Arrange: a traverse entry covers C:\SharedDir (its AllAppliedPaths list).
        // A separate allow grant exists for a file inside C:\SharedDir, so GetGrantPaths
        // includes C:\SharedDir. When RemoveTraverse is called with updateFileSystem=true,
        // RevertForPath must NOT remove the traverse ACE from C:\SharedDir.
        const string grantFilePath = @"C:\SharedDir\File.exe";
        const string traversePath = @"C:\SomeOtherDir";
        const string sharedDir = @"C:\SharedDir";

        // Add a grant for the file so grantPaths will contain C:\SharedDir
        _service.AddGrant(UserSid, grantFilePath, isDeny: false, ReadOnly);

        // Insert a traverse entry whose AllAppliedPaths includes the grant-needed dir
        var extraTraverse = new GrantedPathEntry
        {
            Path = traversePath,
            IsTraverseOnly = true,
            AllAppliedPaths = [sharedDir] // this dir is protected by the grant above
        };
        _database.GetOrCreateAccount(UserSid).Grants.Add(extraTraverse);

        // Track whether RemoveTraverseOnlyAce is called for sharedDir
        bool removedAceOnSharedDir = false;
        _traverseAcl.Setup(t => t.RemoveTraverseOnlyAce(
                It.Is<string>(p => string.Equals(p, sharedDir, StringComparison.OrdinalIgnoreCase)),
                It.IsAny<SecurityIdentifier>()))
            .Callback(() => removedAceOnSharedDir = true);

        // Act: remove the extra traverse entry (which references sharedDir in its applied paths)
        _service.RemoveTraverse(UserSid, traversePath, updateFileSystem: true);

        // Assert: the ACE for sharedDir was NOT removed because the grant still needs it
        Assert.False(removedAceOnSharedDir);
    }

    // --- ChangeOwner / ResetOwner ---

    [Fact]
    public void ChangeOwner_ForwardsToNtfsHelper()
    {
        // Arrange: build service with a mocked IGrantNtfsHelper so the forwarding can be verified
        var service = BuildServiceWithMockedNtfs(out var ntfsMock, out _);

        // Act
        service.ChangeOwner(TestPath, UserSid, recursive: true);

        // Assert: forwarded to ntfsHelper.ChangeOwner with the exact same arguments
        ntfsMock.Verify(n => n.ChangeOwner(TestPath, UserSid, true), Times.Once);
    }

    [Fact]
    public void ResetOwner_ForwardsToNtfsHelper()
    {
        // Arrange: build service with a mocked IGrantNtfsHelper
        var service = BuildServiceWithMockedNtfs(out var ntfsMock, out _);

        // Act
        service.ResetOwner(TestPath, recursive: false);

        // Assert: forwarded to ntfsHelper.ResetOwner with the exact same arguments
        ntfsMock.Verify(n => n.ResetOwner(TestPath, false), Times.Once);
    }

    // --- RemoveAll ---

    [Fact]
    public void RemoveAll_ClearsAllGrantsAndTraverseEntries()
    {
        // Arrange: add a grant (which also auto-adds traverse)
        _service.AddGrant(UserSid, TestPath, isDeny: false, ReadOnly);
        Assert.NotEmpty(_database.GetAccount(UserSid)?.Grants ?? []);

        // Act
        _service.RemoveAll(UserSid, updateFileSystem: true);

        // Assert: all grants cleared
        var grants = _database.GetAccount(UserSid)?.Grants;
        Assert.True(grants == null || grants.Count == 0);

        // Verify NTFS revert was called for the grant
        _aclAccessor.Verify(a => a.RemoveExplicitAces(TestPath, UserSid,
            AccessControlType.Allow), Times.AtLeastOnce);
    }

    [Fact]
    public void RemoveAll_ContainerSid_RevertsIuGrantsWithSavedRightsEquality()
    {
        // Arrange: container and IU both have same grant
        var db = new AppDatabase();
        var service = BuildServiceWithIuResolver(db, InteractiveSid);

        service.AddGrant(ContainerSid, TestPath, isDeny: false, ReadOnly);

        // Verify IU has the matching grant
        var iuBefore = db.GetAccount(InteractiveSid)?.Grants
            .FirstOrDefault(e => !e.IsTraverseOnly && !e.IsDeny && e.Path == TestPath);
        Assert.NotNull(iuBefore);

        // Act
        service.RemoveAll(ContainerSid, updateFileSystem: true);

        // Assert: IU grant removed (SavedRights matched)
        var iuEntry = db.GetAccount(InteractiveSid)?.Grants
            .FirstOrDefault(e => !e.IsTraverseOnly && !e.IsDeny && e.Path == TestPath);
        Assert.Null(iuEntry);
    }

    [Fact]
    public void RemoveAll_ContainerSid_IuGrantPreservedWhenOtherContainerNeedsPath()
    {
        // Arrange: two containers both need TestPath; IU grant present for both
        var db = new AppDatabase();
        var service = BuildServiceWithIuResolver(db, InteractiveSid);

        service.AddGrant(ContainerSid, TestPath, isDeny: false, ReadOnly);
        service.AddGrant(OtherContainerSid, TestPath, isDeny: false, ReadOnly);

        // Act: remove first container
        service.RemoveAll(ContainerSid, updateFileSystem: true);

        // Assert: IU grant preserved because OtherContainerSid still needs the path
        var iuEntry = db.GetAccount(InteractiveSid)?.Grants
            .FirstOrDefault(e => !e.IsTraverseOnly && !e.IsDeny && e.Path == TestPath);
        Assert.NotNull(iuEntry);
    }

    [Fact]
    public void RemoveAll_ContainerSid_IuGrantPreservedWhenSavedRightsDiffer()
    {
        // Arrange: container has ReadOnly rights but IU grant has ReadExecute (different source)
        var db = new AppDatabase();
        var service = BuildServiceWithIuResolver(db, InteractiveSid);

        service.AddGrant(ContainerSid, TestPath, isDeny: false, ReadOnly);

        // Override the IU's SavedRights to simulate a different origin
        var iuEntry = db.GetOrCreateAccount(InteractiveSid).Grants
            .First(e => !e.IsTraverseOnly && !e.IsDeny && e.Path == TestPath);
        iuEntry.SavedRights = ReadExecute;

        // Act
        service.RemoveAll(ContainerSid, updateFileSystem: true);

        // Assert: IU grant preserved because SavedRights differ from container's
        var preserved = db.GetAccount(InteractiveSid)?.Grants
            .FirstOrDefault(e => !e.IsTraverseOnly && !e.IsDeny && e.Path == TestPath);
        Assert.NotNull(preserved);
    }

    [Fact]
    public void RemoveAll_UpdateFileSystemFalse_ClearsDbWithoutNtfsRevert()
    {
        // Arrange: add a grant (which also auto-adds traverse)
        _service.AddGrant(UserSid, TestPath, isDeny: false, ReadOnly);
        Assert.NotEmpty(_database.GetAccount(UserSid)?.Grants ?? []);

        // Reset invocation tracking after AddGrant so only RemoveAll calls are observed
        _aclAccessor.Invocations.Clear();

        // Act: DB-only clear — no NTFS revert
        _service.RemoveAll(UserSid, updateFileSystem: false);

        // Assert: all DB grants cleared
        var grants = _database.GetAccount(UserSid)?.Grants;
        Assert.True(grants == null || grants.Count == 0);

        // Assert: no NTFS calls made
        _aclAccessor.Verify(a => a.RemoveExplicitAces(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<AccessControlType>()), Times.Never);
    }

    // --- UpdateFromPath ---

    [Fact]
    public void UpdateFromPath_NonExistentPath_ReturnsFalse()
    {
        var result = _service.UpdateFromPath(@"C:\DoesNotExistNever\file.exe", UserSid);

        Assert.False(result);
    }

    [Fact]
    public void UpdateFromPath_DiscoverGrant_CreatesDbEntry()
    {
        // Arrange: path exists; ACL has an allow ACE for UserSid with ReadMask (not traverse-only)
        var tempDir = Path.GetTempPath();
        var security = CreateSecurityWithAllowAce(UserSid, GrantRightsMapper.ReadMask);
        _aclAccessor.Setup(a => a.GetSecurity(tempDir)).Returns(security);

        // Act
        var modified = _service.UpdateFromPath(tempDir, UserSid);

        // Assert: grant entry created
        Assert.True(modified);
        var entry = _database.GetAccount(UserSid)?.Grants
            .FirstOrDefault(e => !e.IsTraverseOnly && !e.IsDeny &&
                string.Equals(e.Path, tempDir, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(entry);
    }

    [Fact]
    public void UpdateFromPath_DiscoverTraverseAce_CreatesTraverseDbEntry()
    {
        // Arrange: path exists; ACL has traverse-only ACE for UserSid
        var tempDir = Path.GetTempPath();
        var security = CreateSecurityWithAllowAce(UserSid, GrantRightsMapper.TraverseOnlyMask);
        _aclAccessor.Setup(a => a.GetSecurity(tempDir)).Returns(security);

        // Act
        var modified = _service.UpdateFromPath(tempDir, UserSid);

        // Assert: traverse entry created
        Assert.True(modified);
        var entry = _database.GetAccount(UserSid)?.Grants
            .FirstOrDefault(e => e.IsTraverseOnly &&
                string.Equals(e.Path, tempDir, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(entry);
    }

    [Fact]
    public void UpdateFromPath_NoNewAces_ReturnsFalse()
    {
        // Arrange: path exists; ACL has an ACE for UserSid that already matches the DB
        var tempDir = Path.GetTempPath();
        var rights = ReadOnly;
        // Pre-populate the DB entry to match what ACL would return
        _database.GetOrCreateAccount(UserSid).Grants.Add(
            new GrantedPathEntry { Path = tempDir, IsDeny = false, SavedRights = rights });

        var security = CreateSecurityWithAllowAce(UserSid, GrantRightsMapper.ReadMask);
        _aclAccessor.Setup(a => a.GetSecurity(tempDir)).Returns(security);

        // Act
        var modified = _service.UpdateFromPath(tempDir, UserSid);

        // Assert: no DB modification (rights already matched)
        Assert.False(modified);
    }

    [Fact]
    public void UpdateFromPath_NullSid_ProcessesAllSidsFoundInAcl()
    {
        // Arrange: path exists; ACL has ACE for UserSid
        var tempDir = Path.GetTempPath();
        var security = CreateSecurityWithAllowAce(UserSid, GrantRightsMapper.ReadMask);
        _aclAccessor.Setup(a => a.GetSecurity(tempDir)).Returns(security);

        // Act — null sid means process any SID found in the ACL
        var modified = _service.UpdateFromPath(tempDir, sid: null);

        // Assert: DB updated for UserSid
        Assert.True(modified);
        var entry = _database.GetAccount(UserSid)?.Grants
            .FirstOrDefault(e => !e.IsTraverseOnly && !e.IsDeny);
        Assert.NotNull(entry);
    }

    // --- Utility: CheckGrantStatus, ReadGrantState, ValidateGrant ---

    [Fact]
    public void CheckGrantStatus_NonExistentPath_ReturnsUnavailable()
    {
        var status = _service.CheckGrantStatus(@"C:\DoesNotExistNever\file.exe",
            UserSid, isDeny: false);

        Assert.Equal(PathAclStatus.Unavailable, status);
    }

    [Fact]
    public void CheckGrantStatus_PathExistsNoMatchingAce_ReturnsBroken()
    {
        // Arrange: path exists (temp dir) but its ACL has no ACE for UserSid in allow mode
        var tempDir = Path.GetTempPath();
        var security = EmptySecurity();
        _aclAccessor.Setup(a => a.GetSecurity(tempDir)).Returns(security);

        // Act
        var status = _service.CheckGrantStatus(tempDir, UserSid, isDeny: false);

        Assert.Equal(PathAclStatus.Broken, status);
    }

    [Fact]
    public void CheckGrantStatus_PathExistsWithMatchingAce_ReturnsAvailable()
    {
        // Arrange: path exists and ACL has allow ACE for UserSid
        var tempDir = Path.GetTempPath();
        var security = CreateSecurityWithAllowAce(UserSid, GrantRightsMapper.ReadMask);
        _aclAccessor.Setup(a => a.GetSecurity(tempDir)).Returns(security);

        // Act
        var status = _service.CheckGrantStatus(tempDir, UserSid, isDeny: false);

        Assert.Equal(PathAclStatus.Available, status);
    }

    [Fact]
    public void ReadGrantState_EmptyAcl_ReturnsAllUncheckedWithZeroAceCounts()
    {
        // Arrange
        var tempDir = Path.GetTempPath();
        _aclAccessor.Setup(a => a.GetSecurity(tempDir)).Returns(EmptySecurity());

        // Act
        var state = _service.ReadGrantState(tempDir, UserSid, []);

        // Assert: all unchecked, no direct ACEs
        Assert.Equal(RightCheckState.Unchecked, state.AllowExecute);
        Assert.Equal(RightCheckState.Unchecked, state.AllowWrite);
        Assert.Equal(0, state.DirectAllowAceCount);
        Assert.Equal(0, state.DirectDenyAceCount);
    }

    [Fact]
    public void ValidateGrant_NoExistingEntries_DoesNotThrow()
    {
        var exception = Record.Exception(
            () => _service.ValidateGrant(UserSid, TestPath, isDeny: false));

        Assert.Null(exception);
    }

    [Fact]
    public void ValidateGrant_SameModeDuplicate_ThrowsInvalidOperationException()
    {
        // Arrange: add allow grant
        _service.AddGrant(UserSid, TestPath, isDeny: false, ReadOnly);

        // Act + Assert: validate another allow for same path throws
        Assert.Throws<InvalidOperationException>(
            () => _service.ValidateGrant(UserSid, TestPath, isDeny: false));
    }

    [Fact]
    public void ValidateGrant_OppositeModeExists_ThrowsInvalidOperationException()
    {
        // Arrange: add allow grant
        _service.AddGrant(UserSid, TestPath, isDeny: false, ReadOnly);

        // Act + Assert: validate deny for same path throws
        Assert.Throws<InvalidOperationException>(
            () => _service.ValidateGrant(UserSid, TestPath, isDeny: true));
    }

    // --- Helpers ---

    private static FileSystemSecurity EmptySecurity()
    {
        // Create a truly empty security descriptor with no ACEs.
        // Do NOT use DirectoryInfo.GetAccessControl() here — that returns the real DACL from the
        // filesystem which may contain ACEs for the current test user (whose SID could equal UserSid
        // on this machine), causing false positives in tests that expect no matching ACE.
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        return security;
    }

    private static FileSystemSecurity CreateSecurityWithAllowAce(string sid, FileSystemRights rights)
    {
        var security = EmptySecurity();
        var identity = new SecurityIdentifier(sid);
        security.AddAccessRule(new FileSystemAccessRule(
            identity, rights, InheritanceFlags.None, PropagationFlags.None,
            AccessControlType.Allow));
        return security;
    }
}
