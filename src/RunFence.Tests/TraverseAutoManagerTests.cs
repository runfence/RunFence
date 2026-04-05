using System.Security.AccessControl;
using Moq;
using RunFence.Acl.Permissions;
using RunFence.Acl.UI;
using RunFence.Core.Models;
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

    private static readonly Mock<IAclPermissionService> DefaultAclPermission = new();

    private static GrantedPathEntry AllowEntry(string path) =>
        new() { Path = path, IsDeny = false, IsTraverseOnly = false };

    private static GrantedPathEntry TraverseEntry(string path) =>
        new() { Path = path, IsTraverseOnly = true };

    private static TraverseAutoManager CreateManager(
        AclManagerPendingChanges pending,
        AppDatabase db,
        IAclPermissionService? aclPermission = null,
        IReadOnlyList<string>? groupSids = null)
    {
        var dbProvider = new LambdaDatabaseProvider(() => db);
        var mgr = new TraverseAutoManager(aclPermission ?? DefaultAclPermission.Object, dbProvider);
        mgr.Initialize(pending, Sid, groupSids ?? []);
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
        // Files that don't exist on disk → falls back to parent dir
        var result = TraverseAutoManager.GetTraversePath(AppPath);
        Assert.Equal(AppDir, result, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetTraversePath_ForExistingDirectory_ReturnsDirectoryItself()
    {
        using var tempDir = new TempDirectory("TraversePathTest");
        var result = TraverseAutoManager.GetTraversePath(tempDir.Path);
        Assert.Equal(tempDir.Path, result, StringComparer.OrdinalIgnoreCase);
    }

    // --- Folder grant traverse ---

    [Fact]
    public void AutoAdd_ForFolderGrant_AddsFolderItselfAsTraversePath()
    {
        // For a folder grant (directory path), the traverse entry must cover the folder itself
        // so the account can traverse into it, not just its parent.
        using var tempDir = new TempDirectory("TraverseFolderTest");
        var pending = new AclManagerPendingChanges();
        var db = MakeDatabase();
        var mgr = CreateManager(pending, db);

        // Act — simulate adding an allow grant for a directory
        var traversePath = TraverseAutoManager.GetTraversePath(tempDir.Path);
        mgr.AutoAddTraverseIfMissing(traversePath!);

        // Assert — traverse entry is for the folder itself, not its parent
        Assert.True(pending.IsPendingTraverseAdd(tempDir.Path));
    }

    [Fact]
    public void AutoRemove_FolderGrant_UsesFolderItselfAsTraverseKey()
    {
        // When removing a folder grant, the traverse entry keyed on the folder itself
        // (not its parent) must be scheduled for removal.
        using var tempDir = new TempDirectory("TraverseFolderRemoveTest");
        var existing = TraverseEntry(tempDir.Path);
        var db = MakeDatabase([existing]);
        var pending = new AclManagerPendingChanges();
        var mgr = CreateManager(pending, db);

        var traversePath = TraverseAutoManager.GetTraversePath(tempDir.Path);
        mgr.AutoRemoveTraverseIfUnneeded(traversePath!);

        Assert.True(pending.IsPendingTraverseRemove(tempDir.Path));
    }

    [Fact]
    public void AutoRemove_FolderGrant_OtherFilGrantInSameFolder_DoesNotRemove()
    {
        // A file grant whose parent is the folder grant's directory depends on the same
        // traverse entry (the folder itself). Removing the folder grant must NOT remove
        // the traverse entry when a file grant inside that folder still exists.
        using var tempDir = new TempDirectory("TraverseFolderSharedTest");
        var fileGrantPath = Path.Combine(tempDir.Path, "app.exe");
        var folderGrantPath = tempDir.Path;

        var existing = TraverseEntry(tempDir.Path);
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

        var traversePath = TraverseAutoManager.GetTraversePath(folderGrantPath);
        mgr.AutoRemoveTraverseIfUnneeded(traversePath!);

        // File grant still needs traverse on the folder → do not remove
        Assert.False(pending.IsPendingTraverseRemove(tempDir.Path));
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
        // When the SID already has effective traverse rights on the real directory (reported
        // by IAclPermissionService), AutoAddTraverseIfMissing must not add a pending entry.
        using var tempDir = new TempDirectory("TraverseAutoTest");
        var aclPermission = new Mock<IAclPermissionService>();

        // Report that traverse rights are already effective on the real temp directory
        aclPermission
            .Setup(a => a.HasEffectiveRights(
                It.IsAny<FileSystemSecurity>(), Sid,
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<FileSystemRights>()))
            .Returns(true);

        var db = MakeDatabase();
        var pending = new AclManagerPendingChanges();
        var mgr = CreateManager(pending, db, aclPermission.Object);

        // Act — path is a real directory that exists on disk; aclPermission says traverse is covered
        mgr.AutoAddTraverseIfMissing(tempDir.Path);

        // Assert — no pending add since traverse is already effective
        Assert.False(pending.IsPendingTraverseAdd(tempDir.Path));
    }

    [Fact]
    public void AutoAdd_AclPermissionReportsNoTraverse_AddsTraverse()
    {
        // When IAclPermissionService reports that the SID does NOT have effective traverse rights,
        // AutoAddTraverseIfMissing must add the traverse entry to PendingTraverseAdds.
        using var tempDir = new TempDirectory("TraverseAutoTest2");
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

        mgr.AutoAddTraverseIfMissing(tempDir.Path);

        // Traverse is not yet effective → must be queued for add
        Assert.True(pending.IsPendingTraverseAdd(tempDir.Path));
    }

    [Fact]
    public void AutoAdd_WithGroupSids_PassedToAclPermissionCheck()
    {
        // When groupSids are provided, they must be forwarded to IAclPermissionService.HasEffectiveRights
        // so the check considers group memberships when determining effective traverse rights.
        using var tempDir = new TempDirectory("TraverseAutoTest3");
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

        mgr.AutoAddTraverseIfMissing(tempDir.Path);

        // Verify group SIDs were forwarded to the permission check
        Assert.NotNull(capturedGroupSids);
        Assert.Contains("S-1-5-32-545", capturedGroupSids!);
    }
}