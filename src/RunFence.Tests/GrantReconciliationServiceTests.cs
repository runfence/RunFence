using System.Security.AccessControl;
using System.Security.Principal;
using Moq;
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
    }

    private GrantReconciliationService BuildService(AppDatabase db) =>
        new(_aclPermission.Object, new Mock<ILocalGroupMembershipService>().Object, _log.Object, _sessionSaver.Object,
            new LambdaDatabaseProvider(() => db));

    // _service uses an empty database; suitable for tests that don't depend on database state.
    private GrantReconciliationService _service => BuildService(new AppDatabase());

    // --- DetectGroupChanges tests ---

    [Fact]
    public async Task DetectGroupChanges_FirstSeenSid_PopulatesSnapshotWithoutReportingChange()
    {
        var db = new AppDatabase
        {
            SidNames =
            {
                [UserSid] = "MACHINE\\alice"
            }
        };
        db.GetOrCreateAccount(UserSid);
        var service = BuildService(db);

        var changed = await service.DetectGroupChanges();

        Assert.Empty(changed);
        Assert.True(db.AccountGroupSnapshots!.ContainsKey(UserSid));
        Assert.Equal(DefaultGroups, db.AccountGroupSnapshots[UserSid]);
    }

    [Fact]
    public async Task DetectGroupChanges_SameGroups_ReturnsEmpty()
    {
        var db = new AppDatabase
        {
            SidNames =
            {
                [UserSid] = "MACHINE\\alice"
            },
            AccountGroupSnapshots = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                [UserSid] = [..DefaultGroups]
            }
        };
        db.GetOrCreateAccount(UserSid);
        var service = BuildService(db);

        var changed = await service.DetectGroupChanges();

        Assert.Empty(changed);
    }

    [Fact]
    public async Task DetectGroupChanges_GroupAdded_ReturnsChangedSid()
    {
        // Simulate user being added to BUILTIN\Users — groups now differ from snapshot
        _aclPermission.Setup(s => s.ResolveAccountGroupSids(UserSid)).Returns(GroupsWithUsers);

        var db = new AppDatabase
        {
            SidNames =
            {
                [UserSid] = "MACHINE\\alice"
            },
            AccountGroupSnapshots = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                [UserSid] = [..DefaultGroups]
            }
        };
        db.GetOrCreateAccount(UserSid);
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

        var db = new AppDatabase
        {
            SidNames =
            {
                [UserSid] = "MACHINE\\alice"
            },
            AccountGroupSnapshots = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                [UserSid] = [..GroupsWithUsers]
            }
        };
        db.GetOrCreateAccount(UserSid);
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

        var db = new AppDatabase
        {
            SidNames =
            {
                [UserSid] = "MACHINE\\alice"
            },
            AccountGroupSnapshots = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                [UserSid] = [..DefaultGroups] // ["S-1-1-0", "S-1-5-11"]
            }
        };
        db.GetOrCreateAccount(UserSid);
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

        var db = new AppDatabase
        {
            SidNames =
            {
                [UserSid] = "MACHINE\\alice"
            },
            AccountGroupSnapshots = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                [UserSid] = [..DefaultGroups]
            }
        };
        db.GetOrCreateAccount(UserSid);
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
        var result = new GrantReconciliationService.ReconciliationResult(
            UpdatedSnapshots: new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                [UserSid] = GroupsWithUsers
            },
            NewTraverseEntries: new Dictionary<string, List<(string, List<string>)>>(StringComparer.OrdinalIgnoreCase),
            RemovedTraversePaths: new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase));

        service.ApplyReconciliationResult(result);

        Assert.Equal(GroupsWithUsers, db.AccountGroupSnapshots![UserSid]);
    }

    [Fact]
    public void ApplyReconciliationResult_AddsNewTraverseEntries()
    {
        var db = new AppDatabase();
        var service = BuildService(db);
        var appliedPaths = new List<string> { @"C:\Users", @"C:\" };
        var result = new GrantReconciliationService.ReconciliationResult(
            UpdatedSnapshots: new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase),
            NewTraverseEntries: new Dictionary<string, List<(string, List<string>)>>(StringComparer.OrdinalIgnoreCase)
            {
                [UserSid] = [(@"C:\Users", appliedPaths)]
            },
            RemovedTraversePaths: new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase));

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

        var result = new GrantReconciliationService.ReconciliationResult(
            UpdatedSnapshots: new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase),
            NewTraverseEntries: new Dictionary<string, List<(string, List<string>)>>(StringComparer.OrdinalIgnoreCase),
            RemovedTraversePaths: new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
            {
                [UserSid] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { traversePath }
            });

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

        var result = new GrantReconciliationService.ReconciliationResult(
            UpdatedSnapshots: new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase),
            NewTraverseEntries: new Dictionary<string, List<(string, List<string>)>>(StringComparer.OrdinalIgnoreCase),
            RemovedTraversePaths: new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
            {
                [UserSid] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { path }
            });

        service.ApplyReconciliationResult(result);

        // Full grant entry preserved — only traverse entries are removed
        var grants = db.GetAccount(UserSid)?.Grants;
        Assert.NotNull(grants);
        Assert.Single(grants);
        Assert.False(grants[0].IsTraverseOnly);
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

        // Assert — snapshots updated for both SIDs even if filesystem reconciliation is skipped
        Assert.True(result.UpdatedSnapshots.ContainsKey(UserSid));
        Assert.Equal(GroupsWithUsers, result.UpdatedSnapshots[UserSid]);
        Assert.True(result.UpdatedSnapshots.ContainsKey(userSid2));
        Assert.Equal(DefaultGroups, result.UpdatedSnapshots[userSid2]);
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

        Assert.Empty(result.RemovedTraversePaths);
    }

    [Fact]
    public void ReconcileChangedSids_EmptyAccountGrants_NoTraverseRemovals()
    {
        // When accountGrants is provided but contains no entry for the SID, nothing is removed.
        var changedSids = new List<(string Sid, List<string> NewGroups)>
        {
            (UserSid, GroupsWithUsers)
        };
        var emptyGrants = new Dictionary<string, List<GrantedPathEntry>>(StringComparer.OrdinalIgnoreCase);

        var result = _service.ReconcileChangedSids(changedSids, accountGrants: emptyGrants);

        Assert.Empty(result.RemovedTraversePaths);
    }

    [Fact]
    public void ReconcileChangedSids_EmptyInput_EmptyResult()
    {
        // Empty input → all result collections empty (no-op)
        var result = _service.ReconcileChangedSids([]);

        Assert.Empty(result.UpdatedSnapshots);
        Assert.Empty(result.NewTraverseEntries);
        Assert.Empty(result.RemovedTraversePaths);
    }

    // --- ReconcileIfGroupsChanged tests ---

    [Fact]
    public async Task ReconcileIfGroupsChanged_NoChanges_ReturnsFalse()
    {
        var db = new AppDatabase
        {
            SidNames =
            {
                [UserSid] = "MACHINE\\alice"
            },
            AccountGroupSnapshots = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                [UserSid] = [..DefaultGroups]
            }
        };
        db.GetOrCreateAccount(UserSid);
        var service = BuildService(db);

        var result = await service.ReconcileIfGroupsChanged();

        Assert.False(result);
    }

    [Fact]
    public async Task ReconcileIfGroupsChanged_FirstRun_ReturnsFalseButPopulatesSnapshot()
    {
        // First run: snapshot is null/missing → populate without reconciling (no change to report)
        var db = new AppDatabase
        {
            SidNames =
            {
                [UserSid] = "MACHINE\\alice"
            }
        };
        db.GetOrCreateAccount(UserSid);
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

        var db = new AppDatabase
        {
            SidNames =
            {
                [UserSid] = "MACHINE\\alice"
            },
            AccountGroupSnapshots = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                [UserSid] = [..DefaultGroups]
            }
        };
        db.GetOrCreateAccount(UserSid);
        var service = BuildService(db);

        var changed = await service.ReconcileIfGroupsChanged();

        // Snapshot updated to new groups
        Assert.True(changed);
        Assert.Equal(GroupsWithUsers, db.AccountGroupSnapshots![UserSid]);
    }

    // --- ReconcileLogonScript / filesystem reconciliation path tests ---

    [Fact]
    public void ReconcileLogonScript_ScriptFileMissing_EarlyReturn_NoEntries()
    {
        // ReconcileLogonScript checks both File.Exists(scriptFile) and Directory.Exists(scriptsDir).
        // Using a fake SID guarantees the per-SID script file ("{sid}_block_login.cmd") does not
        // exist in the scripts directory, so the method returns immediately without any side effects.
        var newTraverseEntries = new Dictionary<string, List<(string, List<string>)>>(StringComparer.OrdinalIgnoreCase);
        var removedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var granter = new AncestorTraverseGranter(_log.Object, _aclPermission.Object);
        var identity = new SecurityIdentifier(UserSid);

        // UserSid is a synthetic SID — no script file for it will exist on any real system
        _service.ReconcileLogonScript(UserSid, identity, DefaultGroups,
            granter, newTraverseEntries, removedPaths);

        Assert.Empty(newTraverseEntries);
        Assert.Empty(removedPaths);
    }

    [Fact]
    public void ReconcileChangedSids_InvalidSidString_LogsWarningAndContinues()
    {
        // When ReconcileTraverseForSid throws because new SecurityIdentifier(sid) rejects the
        // SID string, the exception must be caught, a warning logged, and processing must
        // continue for remaining SIDs. Both SIDs must appear in UpdatedSnapshots (snapshot
        // update happens before the traversal attempt).
        //
        // HasEffectiveRights is set to true so that if the second SID's traversal reaches any
        // real directory (e.g. DragBridge), ACEs are not written (traverse already covered).
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

        // Snapshot updated for both entries regardless of per-SID failure
        Assert.True(result.UpdatedSnapshots.ContainsKey("INVALID-SID-STRING"));
        Assert.True(result.UpdatedSnapshots.ContainsKey(userSid2));
        // Warning must be logged for the invalid SID
        _log.Verify(l => l.Warn(It.Is<string>(s => s.Contains("INVALID-SID-STRING"))), Times.Once);
    }

    [Fact]
    public void ReconcileChangedSids_ValidSidNoTraversePaths_OnlySnapshotUpdated()
    {
        // A valid SID with no logon script file (fake SID guarantees this) and empty AccountGrants
        // has nothing to remove. HasEffectiveRights is set to true so that traversal of any
        // real directories (e.g. DragBridge if RunFence is installed) reports traverse already
        // covered — preventing ACE writes and keeping NewTraverseEntries empty.
        _aclPermission.Setup(a => a.HasEffectiveRights(
                It.IsAny<FileSystemSecurity>(),
                It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<FileSystemRights>()))
            .Returns(true);

        var changedSids = new List<(string Sid, List<string> NewGroups)>
        {
            (UserSid, GroupsWithUsers)
        };
        var emptyGrants = new Dictionary<string, List<GrantedPathEntry>>(StringComparer.OrdinalIgnoreCase);

        var result = _service.ReconcileChangedSids(changedSids, emptyGrants);

        Assert.True(result.UpdatedSnapshots.ContainsKey(UserSid));
        Assert.Equal(GroupsWithUsers, result.UpdatedSnapshots[UserSid]);
        // Traverse already effective everywhere → no new ACEs added → NewTraverseEntries empty
        Assert.Empty(result.NewTraverseEntries);
        // No traverse entries in AccountGrants for this SID → nothing to remove
        Assert.Empty(result.RemovedTraversePaths);
    }
}