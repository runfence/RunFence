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

public class GrantReconciliationServiceTests
{
    private const string UserSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
    private const string ContainerSid = "S-1-15-2-99";

    private static readonly List<string> DefaultGroups = ["S-1-1-0", "S-1-5-11"];
    private static readonly List<string> GroupsWithUsers = ["S-1-1-0", "S-1-5-11", "S-1-5-32-545"];

    private readonly Mock<IAclPermissionService> _aclPermission = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<ISessionSaver> _sessionSaver = new();
    private readonly TestFileSystemPathInfo _pathInfo = new();

    public GrantReconciliationServiceTests()
    {
        _aclPermission.Setup(s => s.ResolveAccountGroupSids(UserSid)).Returns(DefaultGroups);
        // Default: report traverse as already effective on any directory so no test ever writes
        // real NTFS ACEs to the production filesystem (e.g. DragBridgeTempDir if RunFence is installed).
        // Individual tests that need specific HasEffectiveRights behaviour override this setup.
        _aclPermission.Setup(s => s.HasEffectiveRights(
                It.IsAny<FileSystemSecurity>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<FileSystemRights>()))
            .Returns(true);

        _service = BuildService(new AppDatabase());
    }

    private GrantReconciliationService BuildService(AppDatabase db, IInteractiveUserResolver? iuResolver = null)
        => BuildService(db, iuResolver, null);

    private GrantReconciliationService BuildService(
        AppDatabase db,
        IInteractiveUserResolver? iuResolver,
        ITraverseAcl? traverseAcl)
    {
        var sidReconciler = new SidReconciler(
            _aclPermission.Object,
            () => new AncestorTraverseGranter(_log.Object, _aclPermission.Object, traverseAcl ?? new Mock<ITraverseAcl>().Object,
                _pathInfo),
            _log.Object,
            iuResolver ?? new Mock<IInteractiveUserResolver>().Object,
            _pathInfo);
        return new GrantReconciliationService(
            _aclPermission.Object, new Mock<ILocalGroupMembershipService>().Object, _log.Object, _sessionSaver.Object,
            new LambdaDatabaseProvider(() => db),
            sidReconciler);
    }

    // _service uses an empty database; suitable for tests that don't depend on database state.
    // Stored as a readonly field so the same instance is reused throughout a single test.
    private readonly GrantReconciliationService _service;

    private static SidReconciler.SidReconciliationResult FindSidResult(
        GrantReconciliationService.ReconciliationResult result,
        string sid)
    {
        var matches = result.SidResults
            .Where(r => string.Equals(r.Sid, sid, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return Assert.Single(matches);
    }

    /// <summary>
    /// Creates an <see cref="AppDatabase"/> with <paramref name="sid"/> in <c>SidNames</c> and
    /// <c>Accounts</c>, and optionally a pre-populated <c>AccountGroupSnapshots</c> entry.
    /// </summary>
    private static AppDatabase MakeDbWithSnapshot(
        string sid,
        string displayName,
        List<string>? snapshotGroups = null)
    {
        var db = new AppDatabase
        {
            SidNames =
            {
                [sid] = displayName
            }
        };
        db.GetOrCreateAccount(sid);
        if (snapshotGroups != null)
        {
            db.AccountGroupSnapshots ??= new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            db.AccountGroupSnapshots[sid] = snapshotGroups;
        }
        return db;
    }

    // --- DetectGroupChanges tests ---

    [Fact]
    public async Task DetectGroupChanges_FirstSeenSid_PopulatesSnapshotWithoutReportingChange()
    {
        var db = MakeDbWithSnapshot(UserSid, "MACHINE\\alice");
        var service = BuildService(db);

        var changed = await service.DetectGroupChanges();

        Assert.Empty(changed);
        Assert.True(db.AccountGroupSnapshots!.ContainsKey(UserSid));
        Assert.Equal(DefaultGroups, db.AccountGroupSnapshots[UserSid]);
    }

    [Fact]
    public async Task DetectGroupChanges_SameGroups_ReturnsEmpty()
    {
        var db = MakeDbWithSnapshot(UserSid, "MACHINE\\alice", [..DefaultGroups]);
        var service = BuildService(db);

        var changed = await service.DetectGroupChanges();

        Assert.Empty(changed);
    }

    [Fact]
    public async Task DetectGroupChanges_GroupAdded_ReturnsChangedSid()
    {
        // Simulate user being added to BUILTIN\Users — groups now differ from snapshot
        _aclPermission.Setup(s => s.ResolveAccountGroupSids(UserSid)).Returns(GroupsWithUsers);

        var db = MakeDbWithSnapshot(UserSid, "MACHINE\\alice", [..DefaultGroups]);
        var service = BuildService(db);

        var changed = await service.DetectGroupChanges();

        Assert.Single(changed);
        Assert.Equal(UserSid, changed[0].Sid);
        Assert.Equal(GroupsWithUsers, changed[0].NewGroups);
    }

    [Fact]
    public async Task DetectGroupChanges_GroupRemoved_ReturnsChangedSid()
    {
        // Snapshot has 3 groups but current only has 2 (removed from BUILTIN\Users)
        _aclPermission.Setup(s => s.ResolveAccountGroupSids(UserSid)).Returns(DefaultGroups);

        var db = MakeDbWithSnapshot(UserSid, "MACHINE\\alice", [..GroupsWithUsers]);
        var service = BuildService(db);

        var changed = await service.DetectGroupChanges();

        Assert.Single(changed);
        Assert.Equal(UserSid, changed[0].Sid);
        Assert.Equal(DefaultGroups, changed[0].NewGroups);
    }

    [Fact]
    public async Task DetectGroupChanges_SameGroupsDifferentOrder_ReturnsEmpty()
    {
        // Order-independence: snapshot has groups in one order, current in another
        // SequenceEqual would incorrectly detect a change; SetEquals should not
        _aclPermission.Setup(s => s.ResolveAccountGroupSids(UserSid))
            .Returns(["S-1-5-11", "S-1-1-0"]); // reversed order

        var db = MakeDbWithSnapshot(UserSid, "MACHINE\\alice", [..DefaultGroups]); // ["S-1-1-0", "S-1-5-11"]
        var service = BuildService(db);

        var changed = await service.DetectGroupChanges();

        Assert.Empty(changed);
    }

    [Fact]
    public async Task DetectGroupChanges_AppContainerSid_IsSkipped()
    {
        // AppContainer SIDs (S-1-15-2-*) have fixed group membership and must not be polled
        var db = new AppDatabase
        {
            SidNames =
            {
                [ContainerSid] = "AppContainer"
            }
        };
        db.GetOrCreateAccount(ContainerSid);
        var service = BuildService(db);

        await service.DetectGroupChanges();

        _aclPermission.Verify(s => s.ResolveAccountGroupSids(ContainerSid), Times.Never);
    }

    [Fact]
    public async Task DetectGroupChanges_SidFromAccounts_AlsoChecked()
    {
        // SIDs in Accounts (not just SidNames) must be included in detection
        var db = new AppDatabase();
        db.GetOrCreateAccount(UserSid);
        var service = BuildService(db);

        await service.DetectGroupChanges();

        _aclPermission.Verify(s => s.ResolveAccountGroupSids(UserSid), Times.Once);
    }

    [Fact]
    public async Task DetectGroupChanges_ResolveGroupSidsFails_SidSkipped()
    {
        _aclPermission.Setup(s => s.ResolveAccountGroupSids(UserSid))
            .Throws(new InvalidOperationException("test error"));

        var db = MakeDbWithSnapshot(UserSid, "MACHINE\\alice", [..DefaultGroups]);
        var service = BuildService(db);

        // Should not throw; SID simply skipped
        var changed = await service.DetectGroupChanges();

        Assert.Empty(changed);
    }

    // --- ApplyReconciliationResult tests ---

    [Fact]
    public void ApplyReconciliationResult_UpdatesSnapshot()
    {
        var db = new AppDatabase();
        var service = BuildService(db);
        var result = new GrantReconciliationService.ReconciliationResult([
            new SidReconciler.SidReconciliationResult(
                UserSid,
                GroupsWithUsers,
                true,
                [],
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                null)
        ]);

        service.ApplyReconciliationResult(result);

        Assert.Equal(GroupsWithUsers, db.AccountGroupSnapshots![UserSid]);
    }

    [Fact]
    public void ApplyReconciliationResult_AddsNewTraverseEntries()
    {
        var db = new AppDatabase();
        var service = BuildService(db);
        var appliedPaths = new List<string> { @"C:\Users", @"C:\" };
        var result = new GrantReconciliationService.ReconciliationResult([
            new SidReconciler.SidReconciliationResult(
                UserSid,
                [],
                true,
                [(@"C:\Users", appliedPaths)],
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                null)
        ]);

        service.ApplyReconciliationResult(result);

        var grants = db.GetAccount(UserSid)?.Grants;
        Assert.NotNull(grants);
        var entry = grants.Single();
        Assert.Equal(@"C:\Users", entry.Path);
        Assert.True(entry.IsTraverseOnly);
        Assert.Equal(appliedPaths, entry.AllAppliedPaths);
    }

    [Fact]
    public void ApplyReconciliationResult_RemovesRedundantTraverseEntries()
    {
        // Issue 39: when a SID gains group membership that covers existing traverse grants,
        // ApplyReconciliationResult removes those traverse entries from AccountGrants.
        // This tests the core of the traverse-removal logic applied to the database.
        var traversePath = Path.GetFullPath(@"C:\ProgramData\RunFence\scripts");
        var db = new AppDatabase();
        db.GetOrCreateAccount(UserSid).Grants.AddRange([
            new GrantedPathEntry { Path = traversePath, IsTraverseOnly = true },
            new GrantedPathEntry { Path = @"C:\Apps\MyApp", IsTraverseOnly = false } // non-traverse entry must be kept
        ]);
        var service = BuildService(db);

        var result = new GrantReconciliationService.ReconciliationResult([
            new SidReconciler.SidReconciliationResult(
                UserSid,
                [],
                true,
                [],
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { traversePath },
                null)
        ]);

        service.ApplyReconciliationResult(result);

        // Traverse entry removed; non-traverse entry preserved
        var remaining = db.GetAccount(UserSid)?.Grants;
        Assert.NotNull(remaining);
        Assert.Single(remaining);
        Assert.False(remaining[0].IsTraverseOnly);
        Assert.Equal(@"C:\Apps\MyApp", remaining[0].Path, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyReconciliationResult_RemovedTraversePaths_OnlyMatchesTraverseEntries()
    {
        // Non-traverse entries on the same path must NOT be removed even if path is in RemovedTraversePaths
        var path = Path.GetFullPath(@"C:\Apps\MyApp");
        var db = new AppDatabase();
        db.GetOrCreateAccount(UserSid).Grants.Add(
            new GrantedPathEntry { Path = path, IsTraverseOnly = false }); // full grant, not traverse
        var service = BuildService(db);

        var result = new GrantReconciliationService.ReconciliationResult([
            new SidReconciler.SidReconciliationResult(
                UserSid,
                [],
                true,
                [],
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { path },
                null)
        ]);

        service.ApplyReconciliationResult(result);

        // Full grant entry preserved — only traverse entries are removed
        var grants = db.GetAccount(UserSid)?.Grants;
        Assert.NotNull(grants);
        Assert.Single(grants);
        Assert.False(grants[0].IsTraverseOnly);
    }

    [Fact]
    public void ApplyReconciliationResult_FailedSid_DoesNotUpdateSnapshot()
    {
        const string invalidSid = "INVALID-SID";
        var db = MakeDbWithSnapshot(invalidSid, "bad", [..DefaultGroups]);
        var service = BuildService(db);
        var result = new GrantReconciliationService.ReconciliationResult([
            new SidReconciler.SidReconciliationResult(
                invalidSid,
                GroupsWithUsers,
                false,
                [],
                new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                "bad sid")
        ]);

        service.ApplyReconciliationResult(result);

        Assert.Equal(DefaultGroups, db.AccountGroupSnapshots![invalidSid]);
        _sessionSaver.Verify(s => s.SaveConfig(), Times.Never);
    }

    // --- ReconcileChangedSids tests ---

    [Fact]
    public void ReconcileChangedSids_PopulatesUpdatedSnapshotsForAllSids()
    {
        // Arrange — two SIDs with changed groups
        const string userSid2 = "S-1-5-21-1111111111-2222222222-3333333333-1002";
        var changedSids = new List<(string Sid, List<string> NewGroups)>
        {
            (UserSid, GroupsWithUsers),
            (userSid2, DefaultGroups)
        };

        // Act
        var result = _service.ReconcileChangedSids(changedSids);

        // Assert — both SIDs have successful reconciliation results with updated groups
        Assert.Equal(GroupsWithUsers, FindSidResult(result, UserSid).NewGroups);
        Assert.Equal(DefaultGroups, FindSidResult(result, userSid2).NewGroups);
    }

    [Fact]
    public void ReconcileChangedSids_NoAccountGrants_NoTraverseRemovals()
    {
        // When accountGrants is null, redundancy checking is disabled — no traverse removals.
        var changedSids = new List<(string Sid, List<string> NewGroups)>
        {
            (UserSid, GroupsWithUsers)
        };

        var result = _service.ReconcileChangedSids(changedSids, accountGrants: null);

        Assert.All(result.SidResults, r => Assert.Empty(r.RemovedTraversePaths));
    }

    [Fact]
    public void ReconcileChangedSids_EmptyAccountGrants_NoTraverseRemovals()
    {
        // When accountGrants is provided but contains no entry for the SID, nothing is removed.
        var changedSids = new List<(string Sid, List<string> NewGroups)>
        {
            (UserSid, GroupsWithUsers)
        };
        var emptyGrants = new Dictionary<string, IReadOnlyList<GrantedPathEntry>>(StringComparer.OrdinalIgnoreCase);

        var result = _service.ReconcileChangedSids(changedSids, accountGrants: emptyGrants);

        Assert.All(result.SidResults, r => Assert.Empty(r.RemovedTraversePaths));
    }

    [Fact]
    public void ReconcileChangedSids_EmptyInput_EmptyResult()
    {
        // Empty input → all result collections empty (no-op)
        var result = _service.ReconcileChangedSids([]);

        Assert.Empty(result.SidResults);
    }

    [Fact]
    public void ReconcileChangedSids_InputSnapshotCanBeIndependentFromLiveDatabase()
    {
        var db = new AppDatabase();
        var liveEntry = new GrantedPathEntry
        {
            Path = @"C:\CloneMe",
            IsTraverseOnly = true,
            AllAppliedPaths = [@"C:\CloneMe", @"C:\"],
            SourceSids = [UserSid]
        };
        db.GetOrCreateAccount(UserSid).Grants.Add(liveEntry);

        var snapshot = db.Accounts.ToDictionary(
            account => account.Sid,
            account => (IReadOnlyList<GrantedPathEntry>)account.Grants
                .Select(entry => entry.Clone())
                .ToArray(),
            StringComparer.OrdinalIgnoreCase);
        var snapEntry = Assert.Single(snapshot[UserSid]);

        liveEntry.AllAppliedPaths!.Add(@"C:\Mutated");
        liveEntry.SourceSids!.Add("S-1-5-21-mutated");

        Assert.DoesNotContain(@"C:\Mutated", snapEntry.AllAppliedPaths ?? []);
        Assert.DoesNotContain("S-1-5-21-mutated", snapEntry.SourceSids ?? []);
    }

    // --- ReconcileIfGroupsChanged tests ---

    [Fact]
    public async Task ReconcileIfGroupsChanged_NoChanges_ReturnsFalse()
    {
        var db = MakeDbWithSnapshot(UserSid, "MACHINE\\alice", [..DefaultGroups]);
        var service = BuildService(db);

        var result = await service.ReconcileIfGroupsChanged();

        Assert.False(result);
    }

    [Fact]
    public async Task ReconcileIfGroupsChanged_FirstRun_ReturnsFalseButPopulatesSnapshot()
    {
        // First run: snapshot is null/missing → populate without reconciling (no change to report)
        var db = MakeDbWithSnapshot(UserSid, "MACHINE\\alice");
        var service = BuildService(db);

        var result = await service.ReconcileIfGroupsChanged();

        Assert.False(result);
        Assert.True(db.AccountGroupSnapshots!.ContainsKey(UserSid));
    }

    [Fact]
    public async Task ReconcileIfGroupsChanged_GroupsChanged_PassesAccountGrantsToReconcile()
    {
        // When groups change and accountGrants is set, ReconcileIfGroupsChanged passes database.AccountGrants
        // to ReconcileChangedSids. Verified via ApplyReconciliationResult updating the snapshot.
        _aclPermission.Setup(s => s.ResolveAccountGroupSids(UserSid)).Returns(GroupsWithUsers);

        var db = MakeDbWithSnapshot(UserSid, "MACHINE\\alice", [..DefaultGroups]);
        var service = BuildService(db);

        var changed = await service.ReconcileIfGroupsChanged();

        // Snapshot updated to new groups
        Assert.True(changed);
        Assert.Equal(GroupsWithUsers, db.AccountGroupSnapshots![UserSid]);
    }

    [Fact]
    public async Task ReconcileIfGroupsChanged_AllSidReconciliationsFail_ReturnsFalseAndLeavesSnapshotDirty()
    {
        const string invalidSid = "INVALID-SID";
        _aclPermission.Setup(s => s.ResolveAccountGroupSids(invalidSid)).Returns(GroupsWithUsers);

        var db = MakeDbWithSnapshot(invalidSid, "bad", [..DefaultGroups]);
        var service = BuildService(db);

        var changed = await service.ReconcileIfGroupsChanged();

        Assert.False(changed);
        Assert.Equal(DefaultGroups, db.AccountGroupSnapshots![invalidSid]);
    }

    // --- Logon script / filesystem reconciliation path tests ---

    [Fact]
    public void ReconcileChangedSids_ScriptFileMissing_EarlyReturn_NoEntries()
    {
        // ReconcileChangedSids -> ReconcileTraverseForSid -> ReconcileLogonScript checks both
        // script file and scripts directory existence through TestFileSystemPathInfo. No fake
        // script file is registered, so ReconcileLogonScript returns without side effects.
        // HasEffectiveRights is true so any fake traverse location is treated as covered.
        _aclPermission.Setup(a => a.HasEffectiveRights(
                It.IsAny<FileSystemSecurity>(),
                It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<FileSystemRights>()))
            .Returns(true);

        var changedSids = new List<(string Sid, List<string> NewGroups)>
        {
            (UserSid, DefaultGroups)
        };
        var emptyGrants = new Dictionary<string, IReadOnlyList<GrantedPathEntry>>(StringComparer.OrdinalIgnoreCase);

        // UserSid is a synthetic SID — no script file for it will exist on any real system
        var result = _service.ReconcileChangedSids(changedSids, emptyGrants);

        var sidResult = FindSidResult(result, UserSid);
        Assert.True(sidResult.Succeeded);
        Assert.Empty(sidResult.RemovedTraversePaths);
    }

    [Fact]
    public void ReconcileChangedSids_InvalidSidString_LogsWarningAndContinues()
    {
        // When ReconcileTraverseForSid throws because new SecurityIdentifier(sid) rejects the
        // SID string, the exception must be caught, a warning logged, and processing must
        // continue for remaining SIDs. Both SIDs must appear in UpdatedSnapshots (snapshot
        // update happens before the traversal attempt).
        //
        // HasEffectiveRights is set to true so any fake traverse location is treated as covered.
        const string userSid2 = "S-1-5-21-9999999999-9999999999-9999999999-1002";
        _aclPermission.Setup(a => a.HasEffectiveRights(
                It.IsAny<FileSystemSecurity>(),
                It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<FileSystemRights>()))
            .Returns(true);

        var changedSids = new List<(string Sid, List<string> NewGroups)>
        {
            // Completely invalid — SecurityIdentifier ctor throws ArgumentException
            ("INVALID-SID-STRING", DefaultGroups),
            // Valid — must still be processed despite earlier failure
            (userSid2, GroupsWithUsers)
        };

        // Should not throw
        var result = _service.ReconcileChangedSids(changedSids);

        var invalidResult = FindSidResult(result, "INVALID-SID-STRING");
        var validResult = FindSidResult(result, userSid2);
        Assert.False(invalidResult.Succeeded);
        Assert.True(validResult.Succeeded);
        // Warning must be logged for the invalid SID
        _log.Verify(l => l.Warn(It.Is<string>(s => s.Contains("INVALID-SID-STRING"))), Times.Once);
    }

    [Fact]
    public void ReconcileChangedSids_ValidSidNoTraversePaths_OnlySnapshotUpdated()
    {
        // A valid SID with no fake existing reconcile locations and empty AccountGrants has
        // nothing to remove.
        _aclPermission.Setup(a => a.HasEffectiveRights(
                It.IsAny<FileSystemSecurity>(),
                It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<FileSystemRights>()))
            .Returns(true);

        var changedSids = new List<(string Sid, List<string> NewGroups)>
        {
            (UserSid, GroupsWithUsers)
        };
        var emptyGrants = new Dictionary<string, IReadOnlyList<GrantedPathEntry>>(StringComparer.OrdinalIgnoreCase);

        var result = _service.ReconcileChangedSids(changedSids, emptyGrants);

        var sidResult = FindSidResult(result, UserSid);
        Assert.True(sidResult.Succeeded);
        Assert.Equal(GroupsWithUsers, sidResult.NewGroups);
        // Traverse already effective everywhere → no new ACEs added
        Assert.Empty(sidResult.NewTraverseEntries);
        // No traverse entries in AccountGrants for this SID → nothing to remove
        Assert.Empty(sidResult.RemovedTraversePaths);
    }

    [Fact]
    public void ReconcileChangedSids_TraverseNowCoveredByGroup_MarkedForRemoval()
    {
        // Arrange: Use a fake DragBridge temp root. The entry path must match the reconcile
        // location exactly so CheckRedundantTraverse finds and flags the entry without reading
        // the real ProgramData tree.
        // HasEffectiveRights returns true → groups alone cover the path → marked for removal.
        var dragBridgeTempRoot = Path.Combine(PathConstants.ProgramDataDir, PathConstants.DragBridgeTempDir);
        _pathInfo.AddDirectory(dragBridgeTempRoot);

        var accountGrants = new Dictionary<string, IReadOnlyList<GrantedPathEntry>>(StringComparer.OrdinalIgnoreCase)
        {
            [UserSid] =
            [
                new GrantedPathEntry
                {
                    Path = dragBridgeTempRoot,
                    IsTraverseOnly = true,
                    AllAppliedPaths = [dragBridgeTempRoot]
                }
            ]
        };

        _aclPermission.Setup(a => a.HasEffectiveRights(
                It.IsAny<FileSystemSecurity>(),
                It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<FileSystemRights>()))
            .Returns(true);

        var changedSids = new List<(string Sid, List<string> NewGroups)>
        {
            (UserSid, GroupsWithUsers)
        };

        // Act
        var result = _service.ReconcileChangedSids(changedSids, accountGrants);

        // Assert: the traverse entry is marked for removal because groups now cover it
        var sidResult = FindSidResult(result, UserSid);
        Assert.Contains(Path.GetFullPath(dragBridgeTempRoot), sidResult.RemovedTraversePaths,
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReconcileChangedSids_GroupAllowButUserDeny_DoesNotRemoveTraverse()
    {
        var dragBridgeTempRoot = Path.Combine(PathConstants.ProgramDataDir, PathConstants.DragBridgeTempDir);
        var security = CreateDirectorySecurity(
            allowSids: [(UserSid, TraverseRightsHelper.TraverseRights), ("S-1-1-0", TraverseRightsHelper.TraverseRights)],
            denySids: [(UserSid, TraverseRightsHelper.TraverseRights)]);
        _pathInfo.AddDirectory(dragBridgeTempRoot, security);

        var accountGrants = new Dictionary<string, IReadOnlyList<GrantedPathEntry>>(StringComparer.OrdinalIgnoreCase)
        {
            [UserSid] =
            [
                new GrantedPathEntry
                {
                    Path = dragBridgeTempRoot,
                    IsTraverseOnly = true,
                    AllAppliedPaths = [dragBridgeTempRoot]
                }
            ]
        };

        var changedSids = new List<(string Sid, List<string> NewGroups)>
        {
            (UserSid, GroupsWithUsers)
        };

        var result = _service.ReconcileChangedSids(changedSids, accountGrants);

        Assert.Empty(FindSidResult(result, UserSid).RemovedTraversePaths);
    }

    [Fact]
    public void ReconcileChangedSids_DirectNonTraverseAllow_RemovesRedundantTraverse()
    {
        var dragBridgeTempRoot = Path.Combine(PathConstants.ProgramDataDir, PathConstants.DragBridgeTempDir);
        var security = CreateDirectorySecurity(
            allowSids:
            [
                (UserSid, TraverseRightsHelper.TraverseRights),
                (UserSid, FileSystemRights.ReadAndExecute)
            ]);
        _pathInfo.AddDirectory(dragBridgeTempRoot, security);

        var accountGrants = new Dictionary<string, IReadOnlyList<GrantedPathEntry>>(StringComparer.OrdinalIgnoreCase)
        {
            [UserSid] =
            [
                new GrantedPathEntry
                {
                    Path = dragBridgeTempRoot,
                    IsTraverseOnly = true,
                    AllAppliedPaths = [dragBridgeTempRoot]
                }
            ]
        };

        var changedSids = new List<(string Sid, List<string> NewGroups)>
        {
            (UserSid, GroupsWithUsers)
        };

        var result = _service.ReconcileChangedSids(changedSids, accountGrants);

        Assert.Contains(Path.GetFullPath(dragBridgeTempRoot), FindSidResult(result, UserSid).RemovedTraversePaths,
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReconcileChangedSids_AddTraverseVerificationFailure_MarksSidFailed()
    {
        var appDir = Path.GetDirectoryName(PathConstants.UnlockCmdPath)!;
        _pathInfo.AddDirectory(appDir, CreateDirectorySecurity());
        _aclPermission.Setup(a => a.HasEffectiveRights(
                It.IsAny<FileSystemSecurity>(),
                It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<FileSystemRights>()))
            .Returns(false);

        var iuResolver = new Mock<IInteractiveUserResolver>();
        iuResolver.Setup(r => r.GetInteractiveUserSid()).Returns(UserSid);
        var service = BuildService(new AppDatabase(), iuResolver.Object);

        var result = service.ReconcileChangedSids(
            [(UserSid, DefaultGroups)],
            new Dictionary<string, IReadOnlyList<GrantedPathEntry>>(StringComparer.OrdinalIgnoreCase));

        Assert.False(FindSidResult(result, UserSid).Succeeded);
    }

    [Fact]
    public void ReconcileChangedSids_RemoveTraverseVerificationFailure_MarksSidFailed()
    {
        var dragBridgeTempRoot = Path.Combine(PathConstants.ProgramDataDir, PathConstants.DragBridgeTempDir);
        var security = CreateDirectorySecurity(
            allowSids:
            [
                (UserSid, TraverseRightsHelper.TraverseRights),
                ("S-1-1-0", TraverseRightsHelper.TraverseRights)
            ]);
        _pathInfo.AddDirectory(dragBridgeTempRoot, security);

        var traverseAcl = new Mock<ITraverseAcl>();
        traverseAcl
            .Setup(a => a.HasExplicitTraverseAce(
                dragBridgeTempRoot,
                It.Is<SecurityIdentifier>(sid => sid.Value == UserSid)))
            .Returns(true);
        traverseAcl
            .Setup(a => a.HasExplicitTraverseAceOrThrow(
                dragBridgeTempRoot,
                It.Is<SecurityIdentifier>(sid => sid.Value == UserSid)))
            .Returns(true);

        var service = BuildService(new AppDatabase(), null, traverseAcl.Object);
        var accountGrants = new Dictionary<string, IReadOnlyList<GrantedPathEntry>>(StringComparer.OrdinalIgnoreCase)
        {
            [UserSid] =
            [
                new GrantedPathEntry
                {
                    Path = dragBridgeTempRoot,
                    IsTraverseOnly = true,
                    AllAppliedPaths = [dragBridgeTempRoot]
                }
            ]
        };

        var result = service.ReconcileChangedSids([(UserSid, GroupsWithUsers)], accountGrants);

        Assert.False(FindSidResult(result, UserSid).Succeeded);
    }

    [Fact]
    public void ReconcileChangedSids_TraverseNowNeededAfterGroupRemoval_PopulatesNewTraverseEntries()
    {
        // Arrange: HasEffectiveRights returns false — groups no longer cover traverse, so a new
        // direct ACE must be requested against a fake app directory. ITraverseAcl is mocked
        // (AddAllowAce is a no-op), so no real NTFS writes occur.
        var appDir = Path.GetDirectoryName(PathConstants.UnlockCmdPath)!;
        var security = CreateDirectorySecurity();
        _pathInfo.AddDirectory(appDir, security);
        _aclPermission.Setup(a => a.HasEffectiveRights(
                It.IsAny<FileSystemSecurity>(),
                It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<FileSystemRights>()))
            .Returns<FileSystemSecurity, string, IReadOnlyList<string>, FileSystemRights>((fs, sid, _, rights) =>
            {
                var identity = new SecurityIdentifier(sid);
                return fs.GetAccessRules(true, false, typeof(SecurityIdentifier))
                    .OfType<FileSystemAccessRule>()
                    .Any(rule =>
                        rule.AccessControlType == AccessControlType.Allow &&
                        rule.IdentityReference is SecurityIdentifier ruleSid &&
                        ruleSid.Equals(identity) &&
                        (rule.FileSystemRights & rights) == rights);
            });

        var changedSids = new List<(string Sid, List<string> NewGroups)>
        {
            (UserSid, DefaultGroups)
        };
        var emptyGrants = new Dictionary<string, IReadOnlyList<GrantedPathEntry>>(StringComparer.OrdinalIgnoreCase);

        var iuResolver = new Mock<IInteractiveUserResolver>();
        iuResolver.Setup(r => r.GetInteractiveUserSid()).Returns(UserSid);
        var db = new AppDatabase();
        var traverseAcl = new Mock<ITraverseAcl>();
        traverseAcl
            .Setup(a => a.AddAllowAce(
                appDir,
                It.Is<SecurityIdentifier>(sid => sid.Value == UserSid)))
            .Callback<string, SecurityIdentifier>((_, sid) =>
            {
                security.AddAccessRule(new FileSystemAccessRule(
                    sid,
                    TraverseRightsHelper.TraverseRights,
                    InheritanceFlags.None,
                    PropagationFlags.None,
                    AccessControlType.Allow));
            });
        var service = BuildService(db, iuResolver.Object, traverseAcl.Object);

        // Act
        var result = service.ReconcileChangedSids(changedSids, emptyGrants);

        // Assert: NewTraverseEntries is populated because HasEffectiveRights=false and the fake
        // app directory exists, causing AncestorTraverseGranter to request ACEs.
        var sidResult = FindSidResult(result, UserSid);
        Assert.True(sidResult.Succeeded);
        Assert.NotEmpty(sidResult.NewTraverseEntries);
    }

    private static DirectorySecurity CreateDirectorySecurity(
        IEnumerable<(string Sid, FileSystemRights Rights)>? allowSids = null,
        IEnumerable<(string Sid, FileSystemRights Rights)>? denySids = null)
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        foreach (var (sid, rights) in allowSids ?? [])
        {
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(sid),
                rights,
                InheritanceFlags.None,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

        foreach (var (sid, rights) in denySids ?? [])
        {
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(sid),
                rights,
                InheritanceFlags.None,
                PropagationFlags.None,
                AccessControlType.Deny));
        }

        return security;
    }
}
