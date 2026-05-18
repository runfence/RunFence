using System.Security.AccessControl;
using System.Security.Principal;
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

/// <summary>
/// Tests for <see cref="TraverseAutoManager"/>: automatic traverse-entry creation and removal
/// triggered by allow-grant add/remove and mode-switch operations in the ACL Manager.
/// </summary>
public class TraverseAutoManagerTests
{
    private const string Sid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
    private const string ContainerSid = "S-1-15-2-99-1-2-3-4-5-6";
    private const string AppPath = @"C:\Apps\MyApp\myapp.exe";
    private const string AppDir = @"C:\Apps\MyApp";
    private const string ParentDir = @"C:\Apps";

    private static AppDatabase MakeDatabase(IEnumerable<GrantedPathEntry>? grants = null)
    {
        var db = new AppDatabase();
        if (grants != null)
        {
            db.GetOrCreateAccount(Sid).Grants = grants.ToList();
        }

        return db;
    }

    private readonly Mock<IAclPermissionService> _defaultAclPermission = new();
    private readonly TestFileSystemPathInfo _pathInfo = new();

    private static GrantedPathEntry AllowEntry(string path) =>
        new() { Path = path, IsDeny = false, IsTraverseOnly = false };

    private static GrantedPathEntry TraverseEntry(string path) =>
        new() { Path = path, IsTraverseOnly = true };

    private TraverseAutoManager CreateManager(
        AclManagerPendingChanges pending,
        AppDatabase db,
        IAclPermissionService? aclPermission = null,
        IReadOnlyList<string>? groupSids = null,
        string sid = Sid)
    {
        var dbProvider = new LambdaDatabaseProvider(() => db);
        var mgr = new TraverseAutoManager(
            aclPermission ?? _defaultAclPermission.Object,
            dbProvider,
            new GrantTraversePathResolver(_pathInfo),
            _pathInfo,
            new TraverseGrantOwnerResolver());
        mgr.Initialize(pending, sid, groupSids ?? []);
        return mgr;
    }

    // --- AutoAddTraverseIfMissing ---

    [Fact]
    public void AutoAdd_NoPriorState_AddsTraverseToPendingTraverseAdds()
    {
        // Arrange
        var pending = new AclManagerPendingChanges();
        var db = MakeDatabase();
        var mgr = CreateManager(pending, db);

        // Act — simulate adding an allow grant for AppPath → its parent AppDir needs traverse
        mgr.AutoAddTraverseIfMissing(AppDir);

        // Assert
        Assert.True(pending.IsPendingTraverseAdd(AppDir));
        var entry = pending.PendingTraverseAdds[AppDir];
        Assert.True(entry.IsTraverseOnly);
    }

    [Fact]
    public void AutoAdd_AlreadyPendingAdd_DoesNotDuplicate()
    {
        // Arrange — traverse already queued for add
        var pending = new AclManagerPendingChanges
        {
            PendingTraverseAdds =
            {
                [AppDir] = TraverseEntry(AppDir)
            }
        };
        var db = MakeDatabase();
        var mgr = CreateManager(pending, db);

        // Act
        mgr.AutoAddTraverseIfMissing(AppDir);

        // Assert — still exactly one entry, not duplicated
        Assert.Single(pending.PendingTraverseAdds);
    }

    [Fact]
    public void AutoAdd_PendingRemoveCancelled_WhenAllowGrantAdded()
    {
        // Arrange — traverse was previously queued for removal
        var pending = new AclManagerPendingChanges();
        var existing = TraverseEntry(AppDir);
        pending.PendingTraverseRemoves[AppDir] = existing;
        var db = MakeDatabase();
        var mgr = CreateManager(pending, db);

        // Act — adding an allow grant that depends on this traverse path should cancel removal
        mgr.AutoAddTraverseIfMissing(AppDir);

        // Assert — scheduled removal cancelled; no new pending add either
        Assert.False(pending.IsPendingTraverseRemove(AppDir));
        Assert.False(pending.IsPendingTraverseAdd(AppDir));
    }

    [Fact]
    public void AutoAdd_AlreadyInDatabase_DoesNotAddAgain()
    {
        // Arrange — traverse entry already persisted in DB
        var db = MakeDatabase([TraverseEntry(AppDir)]);
        var pending = new AclManagerPendingChanges();
        var mgr = CreateManager(pending, db);

        // Act
        mgr.AutoAddTraverseIfMissing(AppDir);

        // Assert — no pending add since DB already covers it
        Assert.False(pending.IsPendingTraverseAdd(AppDir));
    }

    [Fact]
    public void AutoAdd_SpecificContainerSharedTraverseAlreadyTracked_DoesNotAddAgain()
    {
        var db = MakeDatabase();
        db.GetOrCreateAccount(AclHelper.AllApplicationPackagesSid).Grants.Add(TraverseEntry(AppDir));
        var pending = new AclManagerPendingChanges();
        var mgr = CreateManager(pending, db, sid: ContainerSid);

        mgr.AutoAddTraverseIfMissing(AppDir);

        Assert.False(pending.IsPendingTraverseAdd(AppDir));
    }

    [Fact]
    public void AutoAdd_SpecificContainerOtherTrackedSharedTraverse_AddsCurrentContainerIntent()
    {
        var db = MakeDatabase();
        db.GetOrCreateAccount(AclHelper.AllApplicationPackagesSid).Grants.Add(new GrantedPathEntry
        {
            Path = AppDir,
            IsTraverseOnly = true,
            SourceSids = ["S-1-15-2-99-1-2-3-4-5-7"]
        });
        var pending = new AclManagerPendingChanges();
        var mgr = CreateManager(pending, db, sid: ContainerSid);

        mgr.AutoAddTraverseIfMissing(AppDir);

        Assert.True(pending.IsPendingTraverseAdd(AppDir));
    }

    // --- AutoRemoveTraverseIfUnneeded ---

    [Fact]
    public void AutoRemove_NoOtherGrantsDependOnPath_SchedulesRemoval()
    {
        // Arrange — traverse in DB, no remaining allow grants for its parent
        var db = MakeDatabase([TraverseEntry(AppDir)]);
        var pending = new AclManagerPendingChanges();
        var mgr = CreateManager(pending, db);

        // Act — the allow grant for AppPath is being removed; AppDir traverse no longer needed
        mgr.AutoRemoveTraverseIfUnneeded(AppDir);

        // Assert
        Assert.True(pending.IsPendingTraverseRemove(AppDir));
    }

    [Fact]
    public void AutoRemove_SpecificContainerManualSharedTraverseNoOtherGrantsDependOnPath_DoesNotScheduleRemoval()
    {
        var db = MakeDatabase();
        var sharedEntry = TraverseEntry(AppDir);
        db.GetOrCreateAccount(AclHelper.AllApplicationPackagesSid).Grants.Add(sharedEntry);
        var pending = new AclManagerPendingChanges();
        var mgr = CreateManager(pending, db, sid: ContainerSid);

        mgr.AutoRemoveTraverseIfUnneeded(AppDir);

        Assert.False(pending.IsPendingTraverseRemove(AppDir));
    }

    [Fact]
    public void AutoRemove_SpecificContainerTrackedSharedTraverseNoOtherGrantsDependOnPath_SchedulesRemoval()
    {
        var db = MakeDatabase();
        var sharedEntry = TraverseEntry(AppDir);
        sharedEntry.SourceSids = [ContainerSid];
        db.GetOrCreateAccount(AclHelper.AllApplicationPackagesSid).Grants.Add(sharedEntry);
        var pending = new AclManagerPendingChanges();
        var mgr = CreateManager(pending, db, sid: ContainerSid);

        mgr.AutoRemoveTraverseIfUnneeded(AppDir);

        Assert.True(pending.PendingTraverseRemoves.TryGetValue(AppDir, out var queued));
        Assert.Same(sharedEntry, queued);
    }

    [Fact]
    public void AutoRemove_SpecificContainerOtherTrackedSharedTraverse_DoesNotScheduleRemoval()
    {
        var db = MakeDatabase();
        db.GetOrCreateAccount(AclHelper.AllApplicationPackagesSid).Grants.Add(new GrantedPathEntry
        {
            Path = AppDir,
            IsTraverseOnly = true,
            SourceSids = ["S-1-15-2-99-1-2-3-4-5-7"]
        });
        var pending = new AclManagerPendingChanges();
        var mgr = CreateManager(pending, db, sid: ContainerSid);

        mgr.AutoRemoveTraverseIfUnneeded(AppDir);

        Assert.False(pending.IsPendingTraverseRemove(AppDir));
    }

    [Fact]
    public void AutoRemove_OtherDbGrantDependsOnPath_DoesNotRemove()
    {
        // Arrange — two allow grants share the same parent (AppDir); only one is being removed
        var db = MakeDatabase([
            AllowEntry(AppPath),
            AllowEntry(@"C:\Apps\MyApp\other.exe"),
            TraverseEntry(AppDir)
        ]);
        var pending = new AclManagerPendingChanges
        {
            PendingRemoves =
            {
                // Simulate removing the first grant (AppPath) as pending
                [(AppPath, false)] = AllowEntry(AppPath)
            }
        };
        var mgr = CreateManager(pending, db);

        // Act — check if traverse is still needed after removing AppPath
        mgr.AutoRemoveTraverseIfUnneeded(AppDir);

        // Assert — second grant still depends on AppDir traverse; do not remove
        Assert.False(pending.IsPendingTraverseRemove(AppDir));
    }

    [Fact]
    public void AutoRemove_PendingAddGrantDependsOnPath_DoesNotRemove()
    {
        // Arrange — traverse in DB; a new (pending) allow grant depends on the parent
        var db = MakeDatabase([TraverseEntry(AppDir)]);
        var pending = new AclManagerPendingChanges
        {
            PendingAdds =
            {
                // Pending allow grant for AppPath → AppDir is still needed
                [(AppPath, false)] = AllowEntry(AppPath)
            }
        };
        var mgr = CreateManager(pending, db);

        // Act
        mgr.AutoRemoveTraverseIfUnneeded(AppDir);

        // Assert
        Assert.False(pending.IsPendingTraverseRemove(AppDir));
    }

    [Fact]
    public void AutoRemove_DbGrantPendingModeSwitchToDeny_TreatedAsNoDependency()
    {
        // R2_D12: an entry with a pending Allow→Deny mode switch must NOT count as an
        // allow dependency when deciding whether to auto-remove the traverse entry.
        var allowEntry = AllowEntry(AppPath);
        var db = MakeDatabase([
            allowEntry,
            TraverseEntry(AppDir)
        ]);
        var pending = new AclManagerPendingChanges
        {
            PendingModifications =
            {
                // Simulate the allow entry having been mode-switched to Deny (pending, not yet applied).
                [(AppPath, false)] = new PendingModification(
                    allowEntry, WasIsDeny: false, WasOwn: false, NewIsDeny: true, NewRights: null)
            }
        };
        var mgr = CreateManager(pending, db);

        // Act — the only allow grant is pending switch to Deny; AppDir traverse is no longer needed.
        mgr.AutoRemoveTraverseIfUnneeded(AppDir);

        // Assert — entry pending switch to Deny is not counted as an allow dependency; traverse removed.
        Assert.True(pending.IsPendingTraverseRemove(AppDir));
    }

    [Fact]
    public void AutoRemove_TraverseWasInPendingAdds_CancelsAdd()
    {
        // Arrange — traverse is only queued as a pending add (not yet in DB); removing grant cancels it
        var db = MakeDatabase();
        var pending = new AclManagerPendingChanges
        {
            PendingTraverseAdds =
            {
                [AppDir] = TraverseEntry(AppDir)
            }
        };
        var mgr = CreateManager(pending, db);

        // Act — the allow grant that required this traverse is being removed
        mgr.AutoRemoveTraverseIfUnneeded(AppDir);

        // Assert — pending add cancelled; no remove queued (nothing in DB to remove)
        Assert.False(pending.IsPendingTraverseAdd(AppDir));
        Assert.False(pending.IsPendingTraverseRemove(AppDir));
    }

    // --- Multi-level ancestors ---

    [Fact]
    public void AutoAdd_ParentAndGrandparent_BothAddedIndependently()
    {
        // Arrange — adding a deeply nested grant; each ancestor can be auto-added separately
        var db = MakeDatabase();
        var pending = new AclManagerPendingChanges();
        var mgr = CreateManager(pending, db);

        // Act — caller typically calls AutoAddTraverseIfMissing for each ancestor level
        mgr.AutoAddTraverseIfMissing(AppDir);
        mgr.AutoAddTraverseIfMissing(ParentDir);

        // Assert — both queued
        Assert.True(pending.IsPendingTraverseAdd(AppDir));
        Assert.True(pending.IsPendingTraverseAdd(ParentDir));
    }

    // --- GetTraversePath (folder vs file) ---

    [Fact]
    public void GetTraversePath_ForFile_ReturnsParentDirectory()
    {
        var mgr = CreateManager(new AclManagerPendingChanges(), MakeDatabase());

        var result = mgr.GetTraversePath(AppPath);

        Assert.Equal(AppDir, result, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetTraversePath_ForExistingDirectory_ReturnsDirectoryItself()
    {
        const string directoryPath = @"C:\Existing\TraversePathTest";
        _pathInfo.AddDirectory(directoryPath);
        var mgr = CreateManager(new AclManagerPendingChanges(), MakeDatabase());

        var result = mgr.GetTraversePath(directoryPath);

        Assert.Equal(directoryPath, result, StringComparer.OrdinalIgnoreCase);
    }

    // --- Folder grant traverse ---

    [Fact]
    public void AutoAdd_ForFolderGrant_AddsFolderItselfAsTraversePath()
    {
        // For a folder grant (directory path), the traverse entry must cover the folder itself
        // so the account can traverse into it, not just its parent.
        const string folderGrantPath = @"C:\Existing\TraverseFolderTest";
        _pathInfo.AddDirectory(folderGrantPath);
        var pending = new AclManagerPendingChanges();
        var db = MakeDatabase();
        var mgr = CreateManager(pending, db);

        // Act — simulate adding an allow grant for a directory
        var traversePath = mgr.GetTraversePath(folderGrantPath);
        mgr.AutoAddTraverseIfMissing(traversePath!);

        // Assert — traverse entry is for the folder itself, not its parent
        Assert.True(pending.IsPendingTraverseAdd(folderGrantPath));
    }

    [Fact]
    public void AutoRemove_FolderGrant_UsesFolderItselfAsTraverseKey()
    {
        // When removing a folder grant, the traverse entry keyed on the folder itself
        // (not its parent) must be scheduled for removal.
        const string folderGrantPath = @"C:\Existing\TraverseFolderRemoveTest";
        _pathInfo.AddDirectory(folderGrantPath);
        var existing = TraverseEntry(folderGrantPath);
        var db = MakeDatabase([existing]);
        var pending = new AclManagerPendingChanges();
        var mgr = CreateManager(pending, db);

        var traversePath = mgr.GetTraversePath(folderGrantPath);
        mgr.AutoRemoveTraverseIfUnneeded(traversePath!);

        Assert.True(pending.IsPendingTraverseRemove(folderGrantPath));
    }

    [Fact]
    public void AutoRemove_FolderGrant_OtherFileGrantInSameFolder_DoesNotRemove()
    {
        // A file grant whose parent is the folder grant's directory depends on the same
        // traverse entry (the folder itself). Removing the folder grant must NOT remove
        // the traverse entry when a file grant inside that folder still exists.
        const string folderGrantPath = @"C:\Existing\TraverseFolderSharedTest";
        _pathInfo.AddDirectory(folderGrantPath);
        var fileGrantPath = Path.Combine(folderGrantPath, "app.exe");

        var existing = TraverseEntry(folderGrantPath);
        var db = MakeDatabase([
            AllowEntry(fileGrantPath),
            AllowEntry(folderGrantPath),
            existing
        ]);
        var pending = new AclManagerPendingChanges
        {
            PendingRemoves =
            {
                // Simulate removing the folder grant
                [(folderGrantPath, false)] = AllowEntry(folderGrantPath)
            }
        };
        var mgr = CreateManager(pending, db);

        var traversePath = mgr.GetTraversePath(folderGrantPath);
        mgr.AutoRemoveTraverseIfUnneeded(traversePath!);

        // File grant still needs traverse on the folder → do not remove
        Assert.False(pending.IsPendingTraverseRemove(folderGrantPath));
    }

    // --- Case insensitivity ---

    [Fact]
    public void AutoAdd_CaseInsensitivePath_RecognisesExistingDbEntry()
    {
        // Arrange — DB entry stored with uppercase; add called with lowercase
        var db = MakeDatabase([new GrantedPathEntry { Path = @"C:\APPS\MYAPP", IsTraverseOnly = true }]);
        var pending = new AclManagerPendingChanges();
        var mgr = CreateManager(pending, db);

        // Act — path casing differs from DB entry
        mgr.AutoAddTraverseIfMissing(@"c:\apps\myapp");

        // Assert — DB entry recognised → no pending add
        Assert.False(pending.IsPendingTraverseAdd(@"c:\apps\myapp"));
    }

    // --- With mocked IAclPermissionService ---

    [Fact]
    public void AutoAdd_AclPermissionReportsTraverseAlreadyEffective_SkipsAdd()
    {
        // When the SID already has effective traverse rights on the fake directory (reported
        // by IAclPermissionService), AutoAddTraverseIfMissing must not add a pending entry.
        const string traversePath = @"C:\Existing\TraverseAutoTest";
        _pathInfo.AddDirectory(traversePath);
        var aclPermission = new Mock<IAclPermissionService>();

        // Report that traverse rights are already effective on the fake directory
        aclPermission
            .Setup(a => a.HasEffectiveRights(
                It.IsAny<FileSystemSecurity>(), Sid,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<FileSystemRights>()))
            .Returns(true);

        var db = MakeDatabase();
        var pending = new AclManagerPendingChanges();
        var mgr = CreateManager(pending, db, aclPermission.Object);

        // Act — path is a fake directory that exists for the service; aclPermission says traverse is covered
        mgr.AutoAddTraverseIfMissing(traversePath);

        // Assert — no pending add since traverse is already effective
        Assert.False(pending.IsPendingTraverseAdd(traversePath));
    }

    [Fact]
    public void AutoAdd_SpecificContainerSid_AllApplicationPackagesAlreadyEffective_SkipsAdd()
    {
        const string traversePath = @"C:\Existing\ContainerTraverseAutoTest";
        _pathInfo.AddDirectory(traversePath);
        var aclPermission = new Mock<IAclPermissionService>();
        aclPermission
            .Setup(a => a.HasEffectiveRights(
                It.IsAny<FileSystemSecurity>(),
                ContainerSid,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<FileSystemRights>()))
            .Returns(false);
        aclPermission
            .Setup(a => a.HasEffectiveRights(
                It.IsAny<FileSystemSecurity>(),
                AclHelper.AllApplicationPackagesSid,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<FileSystemRights>()))
            .Returns(true);

        var pending = new AclManagerPendingChanges();
        var mgr = CreateManager(
            pending,
            MakeDatabase(),
            aclPermission.Object,
            groupSids: [],
            sid: ContainerSid);

        mgr.AutoAddTraverseIfMissing(traversePath);

        Assert.False(pending.IsPendingTraverseAdd(traversePath));
    }

    [Fact]
    public void AutoAdd_AclPermissionReportsNoTraverse_AddsTraverse()
    {
        // When IAclPermissionService reports that the SID does NOT have effective traverse rights,
        // AutoAddTraverseIfMissing must add the traverse entry to PendingTraverseAdds.
        const string traversePath = @"C:\Existing\TraverseAutoTest2";
        _pathInfo.AddDirectory(traversePath);
        var aclPermission = new Mock<IAclPermissionService>();

        // Report that traverse rights are NOT yet present
        aclPermission
            .Setup(a => a.HasEffectiveRights(
                It.IsAny<FileSystemSecurity>(), Sid,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<FileSystemRights>()))
            .Returns(false);

        var db = MakeDatabase();
        var pending = new AclManagerPendingChanges();
        var mgr = CreateManager(pending, db, aclPermission.Object);

        mgr.AutoAddTraverseIfMissing(traversePath);

        // Traverse is not yet effective → must be queued for add
        Assert.True(pending.IsPendingTraverseAdd(traversePath));
    }

    [Fact]
    public void AutoAdd_WithGroupSids_PassedToAclPermissionCheck()
    {
        // When groupSids are provided, they must be forwarded to IAclPermissionService.HasEffectiveRights
        // so the check considers group memberships when determining effective traverse rights.
        const string traversePath = @"C:\Existing\TraverseAutoTest3";
        _pathInfo.AddDirectory(traversePath);
        var aclPermission = new Mock<IAclPermissionService>();
        var groupSids = new List<string> { "S-1-5-32-545" }; // BUILTIN\Users

        IReadOnlyList<string>? capturedGroupSids = null;
        aclPermission
            .Setup(a => a.HasEffectiveRights(
                It.IsAny<FileSystemSecurity>(), Sid,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<FileSystemRights>()))
            .Callback<FileSystemSecurity, string, IReadOnlyList<string>, FileSystemRights>((_, _, groups, _) => capturedGroupSids = groups)
            .Returns(true); // traverse already effective → no pending add

        var db = MakeDatabase();
        var pending = new AclManagerPendingChanges();
        var mgr = CreateManager(pending, db, aclPermission.Object, groupSids);

        mgr.AutoAddTraverseIfMissing(traversePath);

        // Verify group SIDs were forwarded to the permission check
        Assert.NotNull(capturedGroupSids);
        Assert.Contains("S-1-5-32-545", capturedGroupSids!);
    }

    [Fact]
    public void AutoAdd_RealAclEvaluation_GroupAllowButUserDeny_AddsTraverse()
    {
        const string traversePath = @"C:\Existing\TraverseConflict";
        var security = CreateDirectorySecurity(
            allowSids: [("S-1-1-0", TraverseRightsHelper.TraverseRights)],
            denySids: [(Sid, TraverseRightsHelper.TraverseRights)]);
        _pathInfo.AddDirectory(traversePath, security);

        var aclPermission = CreateRealAclPermissionService();
        var pending = new AclManagerPendingChanges();
        var mgr = CreateManager(pending, MakeDatabase(), aclPermission, groupSids: ["S-1-1-0"]);

        mgr.AutoAddTraverseIfMissing(traversePath);

        Assert.True(pending.IsPendingTraverseAdd(traversePath));
    }

    [Fact]
    public void AutoAdd_RealAclEvaluation_DirectNonTraverseAllow_SkipsTraverse()
    {
        const string traversePath = @"C:\Existing\TraverseCovered";
        var security = CreateDirectorySecurity(
            allowSids: [(Sid, FileSystemRights.ReadAndExecute)]);
        _pathInfo.AddDirectory(traversePath, security);

        var aclPermission = CreateRealAclPermissionService();
        var pending = new AclManagerPendingChanges();
        var mgr = CreateManager(pending, MakeDatabase(), aclPermission, groupSids: ["S-1-1-0"]);

        mgr.AutoAddTraverseIfMissing(traversePath);

        Assert.False(pending.IsPendingTraverseAdd(traversePath));
    }

    private static IAclPermissionService CreateRealAclPermissionService()
        => new AclPermissionService(
            new NTTranslateApi(new Mock<ILoggingService>().Object),
            new GroupMembershipApi(new Mock<ILoggingService>().Object),
            new Mock<ILocalGroupQueryService>().Object,
            AclAccessorFactory.Create(),
            new DeterministicAclAccessEvaluator());

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

