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
/// Integration tests for <see cref="PathGrantService"/> against real NTFS ACLs on temp directories.
/// Tests run as non-elevated user; all paths are in the current user's temp directory (owned by the
/// test user) so DACL modifications succeed without elevation.
/// A fake SID is used for ACE operations — Windows accepts any syntactically valid SID for NTFS
/// ACEs regardless of whether it resolves to an account. NTFS reads use <c>includeInherited: false</c>
/// throughout, so even if the fake SID happens to match the real user's SID on a given machine,
/// only explicitly-set ACEs are observed.
/// <see cref="IAclPermissionService"/> is mocked so no group-membership OS calls are made.
/// <see cref="Dispose"/> calls <see cref="IPathGrantService.RemoveAll"/> to clean up any NTFS ACEs
/// (including traverse ACEs on ancestor directories) written during tests.
/// </summary>
public class PathGrantServiceIntegrationTests : IDisposable
{
    // Fake SID used for all NTFS ACE operations in these tests.
    // Windows NTFS accepts any syntactically valid SID without resolving it to an account.
    private const string TestSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";

    private readonly TempDirectory _tempDir = new("PathGrantIntegration");
    private readonly AppDatabase _database = new();
    private readonly Mock<IAclPermissionService> _aclPermission = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly IPathGrantService _service;

    private static readonly IUiThreadInvoker SyncInvoker =
        new LambdaUiThreadInvoker(a => a(), a => a(), a => a());

    public PathGrantServiceIntegrationTests()
    {
        // Default: no group SIDs for TestSid. Individual tests may override HasEffectiveRights.
        _aclPermission.Setup(a => a.ResolveAccountGroupSids(TestSid)).Returns([]);

        var acl = new AclAccessor();
        var ntfsHelper = new GrantNtfsHelper(acl, _log.Object);
        var traverseAcl = new TraverseAcl();
        var ancestorGranter = new AncestorTraverseGranter(_log.Object, _aclPermission.Object, traverseAcl);
        var iuResolver = new Mock<IInteractiveUserResolver>();
        iuResolver.Setup(r => r.GetInteractiveUserSid()).Returns((string?)null);

        var dbAccessor = new UiThreadDatabaseAccessor(new LambdaDatabaseProvider(() => _database), SyncInvoker);
        var grantCore = new GrantCoreOperations(ntfsHelper, dbAccessor, _log.Object);
        var traverseCore = new TraverseCoreOperations(traverseAcl,
            ancestorGranter, _aclPermission.Object, dbAccessor, _log.Object);
        var containerIuSync = new ContainerInteractiveUserSync(grantCore, traverseCore,
            iuResolver.Object, _aclPermission.Object, dbAccessor, _log.Object);
        var syncService = new PathGrantSyncService(dbAccessor, ntfsHelper, _log.Object);
        _service = new PathGrantService(grantCore, traverseCore, ntfsHelper,
            iuResolver.Object, _aclPermission.Object, dbAccessor, containerIuSync, syncService);
    }

    public void Dispose()
    {
        // Clean up any NTFS ACEs written by tests, including traverse ACEs on ancestor directories
        // that outlive the temp directory itself (TempDirectory.Dispose only deletes the leaf dir).
        try { _service.RemoveAll(TestSid, updateFileSystem: true); }
        catch { /* best-effort: ancestor dirs may not be accessible */ }
        _tempDir.Dispose();
    }

    // --- Helpers ---

    private bool HasExplicitAce(string path, bool isDeny)
    {
        var security = new AclAccessor().GetSecurity(path);
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier));
        var sid = new SecurityIdentifier(TestSid);
        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.IdentityReference is not SecurityIdentifier ruleSid || !ruleSid.Equals(sid))
                continue;
            bool isMatchingType = isDeny
                ? rule.AccessControlType == AccessControlType.Deny
                : rule.AccessControlType == AccessControlType.Allow;
            if (isMatchingType)
                return true;
        }
        return false;
    }

    private bool HasTraverseAce(string path)
    {
        if (!Directory.Exists(path)) return false;
        var security = new DirectorySecurity(path, AccessControlSections.Access);
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier));
        var sid = new SecurityIdentifier(TestSid);
        return rules.Cast<FileSystemAccessRule>().Any(rule =>
            rule.AccessControlType == AccessControlType.Allow &&
            rule.IdentityReference.Equals(sid) &&
            rule.FileSystemRights == TraverseRightsHelper.TraverseRights &&
            rule.InheritanceFlags == InheritanceFlags.None);
    }

    // --- AddGrant ---

    [Fact]
    public void AddGrant_Allow_NtfsAceWrittenAndDbEntryAdded()
    {
        // Arrange
        var file = Path.Combine(_tempDir.Path, "grant_allow.txt");
        File.WriteAllText(file, "test");

        // Act
        var result = _service.AddGrant(TestSid, file, isDeny: false,
            new SavedRightsState(Execute: false, Write: false, Read: true, Special: false, Own: false));

        // Assert: explicit allow ACE present on NTFS
        Assert.True(HasExplicitAce(file, isDeny: false),
            "Expected an explicit Allow ACE on the file after AddGrant");

        // Assert: DB entry recorded
        var grants = _database.GetAccount(TestSid)?.Grants;
        Assert.NotNull(grants);
        Assert.Contains(grants, e => string.Equals(e.Path, file, StringComparison.OrdinalIgnoreCase) && !e.IsDeny && !e.IsTraverseOnly);

        Assert.True(result.GrantAdded);
        Assert.True(result.DatabaseModified);
    }

    [Fact]
    public void AddGrant_Deny_NtfsAceWrittenAndDbEntryAdded()
    {
        // Arrange
        var file = Path.Combine(_tempDir.Path, "grant_deny.txt");
        File.WriteAllText(file, "test");

        // Act
        _service.AddGrant(TestSid, file, isDeny: true,
            new SavedRightsState(Execute: false, Write: true, Read: false, Special: true, Own: false));

        // Assert: explicit deny ACE present on NTFS
        Assert.True(HasExplicitAce(file, isDeny: true),
            "Expected an explicit Deny ACE on the file after AddGrant(isDeny=true)");

        // Assert: DB entry with IsDeny=true
        var grants = _database.GetAccount(TestSid)?.Grants;
        Assert.NotNull(grants);
        Assert.Contains(grants, e => string.Equals(e.Path, file, StringComparison.OrdinalIgnoreCase) && e.IsDeny && !e.IsTraverseOnly);
    }

    [Fact]
    public void AddGrant_Duplicate_UpdatesSavedRightsWithoutAddingEntry()
    {
        // Arrange
        var file = Path.Combine(_tempDir.Path, "grant_dup.txt");
        File.WriteAllText(file, "test");
        var firstRights = new SavedRightsState(Execute: false, Write: false, Read: true, Special: false, Own: false);
        _service.AddGrant(TestSid, file, isDeny: false, firstRights);

        var updatedRights = new SavedRightsState(Execute: true, Write: false, Read: true, Special: false, Own: false);

        // Act: add same path again with different rights
        var result = _service.AddGrant(TestSid, file, isDeny: false, updatedRights);

        // Assert: only one non-traverse grant entry (no new entry added)
        var grants = _database.GetAccount(TestSid)?.Grants;
        Assert.NotNull(grants);
        Assert.Single(grants, e => !e.IsTraverseOnly && !e.IsDeny);

        // Assert: SavedRights updated in-place
        var entry = grants.First(e => !e.IsTraverseOnly && !e.IsDeny);
        Assert.True(entry.SavedRights?.Execute);

        Assert.False(result.GrantAdded); // no new DB entry
        Assert.True(result.DatabaseModified); // rights were updated
    }

    // --- RemoveGrant ---

    [Fact]
    public void RemoveGrant_Allow_AceRemovedAndDbEntryRemoved()
    {
        // Arrange
        var file = Path.Combine(_tempDir.Path, "remove_allow.txt");
        File.WriteAllText(file, "test");
        _service.AddGrant(TestSid, file, isDeny: false,
            new SavedRightsState(Execute: false, Write: false, Read: true, Special: false, Own: false));
        Assert.True(HasExplicitAce(file, isDeny: false));

        // Act
        bool removed = _service.RemoveGrant(TestSid, file, isDeny: false, updateFileSystem: true);

        // Assert: ACE removed from NTFS
        Assert.True(removed);
        Assert.False(HasExplicitAce(file, isDeny: false),
            "Expected allow ACE to be removed after RemoveGrant");

        // Assert: grant DB entry removed; traverse entry for parent is also cleaned (no other grants)
        var grants = _database.GetAccount(TestSid)?.Grants;
        Assert.DoesNotContain(grants ?? [], e =>
            string.Equals(e.Path, file, StringComparison.OrdinalIgnoreCase) && !e.IsDeny && !e.IsTraverseOnly);
    }

    [Fact]
    public void RemoveGrant_Deny_AceRemovedAndDbEntryRemoved()
    {
        // Arrange
        var file = Path.Combine(_tempDir.Path, "remove_deny.txt");
        File.WriteAllText(file, "test");
        _service.AddGrant(TestSid, file, isDeny: true,
            new SavedRightsState(Execute: false, Write: true, Read: false, Special: true, Own: false));
        Assert.True(HasExplicitAce(file, isDeny: true));

        // Act
        bool removed = _service.RemoveGrant(TestSid, file, isDeny: true, updateFileSystem: true);

        // Assert: deny ACE removed from NTFS
        Assert.True(removed);
        Assert.False(HasExplicitAce(file, isDeny: true),
            "Expected deny ACE to be removed after RemoveGrant(isDeny=true)");
    }

    [Fact]
    public void RemoveGrant_UpdateFileSystemFalse_AcePreservedDbEntryRemoved()
    {
        // Arrange
        var file = Path.Combine(_tempDir.Path, "remove_no_fs.txt");
        File.WriteAllText(file, "test");
        _service.AddGrant(TestSid, file, isDeny: false,
            new SavedRightsState(Execute: false, Write: false, Read: true, Special: false, Own: false));

        // Act: DB-only remove (untrack without reverting NTFS)
        bool removed = _service.RemoveGrant(TestSid, file, isDeny: false, updateFileSystem: false);

        // Assert: ACE preserved on NTFS, DB entry gone
        Assert.True(removed);
        Assert.True(HasExplicitAce(file, isDeny: false),
            "Expected NTFS ACE to be preserved when updateFileSystem=false");
        var grants = _database.GetAccount(TestSid)?.Grants;
        Assert.DoesNotContain(grants ?? [], e =>
            string.Equals(e.Path, file, StringComparison.OrdinalIgnoreCase) && !e.IsDeny && !e.IsTraverseOnly);
    }

    // --- AddTraverse ---

    [Fact]
    public void AddTraverse_Directory_TraverseAceAddedAndDbEntryAdded()
    {
        // Arrange: HasEffectiveRights=false so AncestorTraverseGranter adds ACEs on each ancestor.
        // ACE writes on non-owned ancestors (e.g. C:\) silently fail — only user-owned dirs succeed.
        var subDir = Path.Combine(_tempDir.Path, "traverse_target");
        Directory.CreateDirectory(subDir);
        _aclPermission.Setup(a => a.HasEffectiveRights(
                It.IsAny<FileSystemSecurity>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<FileSystemRights>()))
            .Returns(false);

        // Act
        var (modified, visitedPaths) = _service.AddTraverse(TestSid, subDir);

        // Assert: traverse ACE written on the target directory
        Assert.True(HasTraverseAce(subDir),
            "Expected traverse ACE on target directory after AddTraverse");

        // Assert: DB entry recorded as IsTraverseOnly
        var grants = _database.GetAccount(TestSid)?.Grants;
        Assert.NotNull(grants);
        Assert.Contains(grants, e =>
            string.Equals(e.Path, subDir, StringComparison.OrdinalIgnoreCase) && e.IsTraverseOnly);

        Assert.NotEmpty(visitedPaths);
        Assert.True(modified);
    }

    [Fact]
    public void AddTraverse_AlreadyHasTraverse_NoAceAddedButPathsStillVisited()
    {
        // Arrange: HasEffectiveRights=true — SID already has effective traverse on all ancestors.
        // AncestorTraverseGranter still records visited paths (for cleanup tracking) but skips ACE writes.
        var subDir = Path.Combine(_tempDir.Path, "traverse_already");
        Directory.CreateDirectory(subDir);
        _aclPermission.Setup(a => a.HasEffectiveRights(
                It.IsAny<FileSystemSecurity>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<string>>(), It.IsAny<FileSystemRights>()))
            .Returns(true);

        // Act
        var (modified, visitedPaths) = _service.AddTraverse(TestSid, subDir);

        // Assert: no traverse ACE written (already covered)
        Assert.False(HasTraverseAce(subDir),
            "No traverse ACE should be written when SID already has effective traverse rights");
        // All ancestor directories are still visited and returned for DB tracking
        Assert.NotEmpty(visitedPaths);
        // DB entry was newly created (modified=true) even though no ACE was added
        Assert.True(modified);
    }

    // --- UpdateFromPath ---

    [Fact]
    public void UpdateFromPath_PathWithExistingAllowAce_DiscoversSidAndCreatesDbEntry()
    {
        // Arrange: write an allow ACE directly to the file (simulating an externally-applied grant)
        var file = Path.Combine(_tempDir.Path, "discover_allow.txt");
        File.WriteAllText(file, "test");
        new AclAccessor().ApplyExplicitAce(file, TestSid, AccessControlType.Allow, GrantRightsMapper.ReadMask);

        // Act
        bool modified = _service.UpdateFromPath(file, TestSid);

        // Assert: DB entry created for the discovered ACE
        Assert.True(modified, "UpdateFromPath should return true when a new DB entry is created");
        var grants = _database.GetAccount(TestSid)?.Grants;
        Assert.NotNull(grants);
        Assert.Contains(grants, e =>
            string.Equals(e.Path, file, StringComparison.OrdinalIgnoreCase) && !e.IsDeny && !e.IsTraverseOnly);
    }

    [Fact]
    public void UpdateFromPath_PathWithDenyAce_DiscoversDenyEntry()
    {
        // Arrange: write a deny ACE directly to the file
        var file = Path.Combine(_tempDir.Path, "discover_deny.txt");
        File.WriteAllText(file, "test");
        new AclAccessor().ApplyExplicitAce(file, TestSid, AccessControlType.Deny,
            GrantRightsMapper.WriteFileMask | GrantRightsMapper.SpecialFileMask);

        // Act
        bool modified = _service.UpdateFromPath(file, TestSid);

        // Assert: deny DB entry created
        Assert.True(modified);
        var grants = _database.GetAccount(TestSid)?.Grants;
        Assert.NotNull(grants);
        Assert.Contains(grants, e =>
            string.Equals(e.Path, file, StringComparison.OrdinalIgnoreCase) && e.IsDeny && !e.IsTraverseOnly);
    }

    [Fact]
    public void UpdateFromPath_NoAceForSid_ReturnsFalse()
    {
        // Arrange: file exists but has no explicit ACE for TestSid
        var file = Path.Combine(_tempDir.Path, "discover_none.txt");
        File.WriteAllText(file, "test");

        // Act
        bool modified = _service.UpdateFromPath(file, TestSid);

        // Assert: no DB entry created, method signals no modification
        Assert.False(modified);
        Assert.Null(_database.GetAccount(TestSid));
    }

    [Fact]
    public void UpdateFromPath_NonExistentPath_ReturnsFalse()
    {
        // Arrange
        var missing = Path.Combine(_tempDir.Path, "does_not_exist.txt");

        // Act
        bool modified = _service.UpdateFromPath(missing, TestSid);

        // Assert
        Assert.False(modified);
    }

    // --- UpdateGrant ---

    [Fact]
    public void UpdateGrant_ChangesRightsOnNtfsAndInDb()
    {
        // Arrange: first add a ReadOnly grant, then update to ReadAndExecute
        var file = Path.Combine(_tempDir.Path, "update_grant.txt");
        File.WriteAllText(file, "test");
        var readOnlyRights = new SavedRightsState(Execute: false, Write: false, Read: true, Special: false, Own: false);
        _service.AddGrant(TestSid, file, isDeny: false, readOnlyRights);

        var readExecRights = new SavedRightsState(Execute: true, Write: false, Read: true, Special: false, Own: false);

        // Act
        _service.UpdateGrant(TestSid, file, isDeny: false, readExecRights);

        // Assert: DB entry reflects updated SavedRights
        var grants = _database.GetAccount(TestSid)?.Grants;
        Assert.NotNull(grants);
        var entry = grants.FirstOrDefault(e =>
            string.Equals(e.Path, file, StringComparison.OrdinalIgnoreCase) && !e.IsDeny && !e.IsTraverseOnly);
        Assert.NotNull(entry);
        Assert.NotNull(entry.SavedRights);
        Assert.True(entry.SavedRights!.Execute, "Execute should be set after UpdateGrant");

        // Assert: updated allow ACE still present on NTFS
        Assert.True(HasExplicitAce(file, isDeny: false),
            "Expected allow ACE still present on NTFS after UpdateGrant");
    }

    // --- CheckGrantStatus ---

    [Fact]
    public void CheckGrantStatus_AfterAddGrant_ReturnsAvailable()
    {
        // Arrange
        var file = Path.Combine(_tempDir.Path, "status_available.txt");
        File.WriteAllText(file, "test");
        _service.AddGrant(TestSid, file, isDeny: false,
            new SavedRightsState(Execute: false, Write: false, Read: true, Special: false, Own: false));

        // Act
        var status = _service.CheckGrantStatus(file, TestSid, isDeny: false);

        // Assert
        Assert.Equal(PathAclStatus.Available, status);
    }

    [Fact]
    public void CheckGrantStatus_NoAce_ReturnsBroken()
    {
        // Arrange: file exists with no explicit ACE for TestSid
        var file = Path.Combine(_tempDir.Path, "status_broken.txt");
        File.WriteAllText(file, "test");

        // Act
        var status = _service.CheckGrantStatus(file, TestSid, isDeny: false);

        // Assert
        Assert.Equal(PathAclStatus.Broken, status);
    }

    [Fact]
    public void CheckGrantStatus_NonExistentPath_ReturnsUnavailable()
    {
        // Arrange
        var missing = Path.Combine(_tempDir.Path, "status_missing.txt");

        // Act
        var status = _service.CheckGrantStatus(missing, TestSid, isDeny: false);

        // Assert
        Assert.Equal(PathAclStatus.Unavailable, status);
    }
}
