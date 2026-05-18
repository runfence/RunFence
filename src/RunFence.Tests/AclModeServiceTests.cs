using System.Security.AccessControl;
using System.Security.Principal;
using Moq;
using RunFence.Acl;
using RunFence.Acl.Permissions;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Tests for <see cref="AclAllowModeService"/> and <see cref="AclDenyModeService"/> that verify
/// the correct ACEs are applied to, and removed from, real filesystem paths in a temporary directory.
/// Both services use <see cref="IAclAccessor.ModifyAclWithFallback"/> on actual NTFS paths — tests assert on
/// the resulting security descriptor to confirm the right rights are present or absent.
///
/// Tests run as the test user who owns the temp directory and can modify its DACL without elevation.
/// </summary>
public class AclModeServiceTests : IDisposable
{
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<ILocalUserProvider> _localUserProvider = new();
    private readonly Mock<IInteractiveUserResolver> _interactiveUserResolver = new();
    private readonly Mock<IDatabaseProvider> _databaseProvider = new();
    private readonly TempDirectory _tempDir = new("AclModeService_Test");

    private readonly AclAllowModeService _allowService;
    private readonly AclDenyModeService _denyService;

    // Well-known SIDs used in allow-mode ACL tests.
    // SYSTEM and Admins receive hardcoded rules regardless of AllowedAclEntries — use WorldSid
    // for entry-specific flag tests so the results are not masked by the hardcoded grants.
    private static readonly string SystemSid =
        new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value;
    private static readonly string WorldSid =
        new SecurityIdentifier(WellKnownSidType.WorldSid, null).Value;

    public AclModeServiceTests()
    {
        var aclAccessor = AclAccessorFactory.Create();
        _allowService = new AclAllowModeService(_log.Object, _localUserProvider.Object, aclAccessor);
        _denyService = new AclDenyModeService(
            _log.Object, _localUserProvider.Object, new ContainerLookupHelper(_databaseProvider.Object),
            _interactiveUserResolver.Object, aclAccessor);

        _databaseProvider.Setup(d => d.GetDatabase()).Returns(new AppDatabase());
        _localUserProvider.Setup(p => p.GetLocalUserAccounts()).Returns([]);
    }

    public void Dispose() => _tempDir.Dispose();

    // ── AclAllowModeService ──────────────────────────────────────────────────

    [Fact]
    public void ApplyAllowAcl_NonExistentPath_ReturnsFalse()
    {
        // Arrange
        var app = CreateAllowApp([new AllowAclEntry { Sid = SystemSid }]);
        var path = Path.Combine(_tempDir.Path, "ghost-file.exe");

        // Act
        var changed = _allowService.ApplyAllowAcl(app, path);

        // Assert: path does not exist → returns false without modifying anything
        Assert.False(changed);
    }

    public static IEnumerable<object?[]> EmptyOrNullAclEntries =>
    [
        [new List<AllowAclEntry>()],
        [null]
    ];

    [Theory]
    [MemberData(nameof(EmptyOrNullAclEntries))]
    public void ApplyAllowAcl_EmptyOrNullAllowedEntries_ReturnsFalse(List<AllowAclEntry>? entries)
    {
        // Both null and empty AllowedAclEntries must short-circuit without modifying the ACL
        var app = CreateAllowApp(entries);
        var filePath = CreateTempFile("no-entries.exe");

        var changed = _allowService.ApplyAllowAcl(app, filePath);

        Assert.False(changed);
    }

    [Fact]
    public void ApplyAllowAcl_File_ProtectsInheritanceAndAddsAllowAces()
    {
        // Arrange: a file app with one allowed SID (SYSTEM — always present on machine)
        var app = CreateAllowApp([new AllowAclEntry { Sid = SystemSid }]);
        var filePath = CreateTempFile("protected.exe");

        // Act
        var changed = _allowService.ApplyAllowAcl(app, filePath);

        // Assert: ACL was modified (inheritance broken, ACEs applied)
        Assert.True(changed);

        var security = new FileInfo(filePath).GetAccessControl();
        Assert.True(security.AreAccessRulesProtected, "Expected inheritance to be broken");

        // SYSTEM gets a hardcoded FullControl Allow ACE
        var rules = GetExplicitAllowRules(security, SystemSid);
        Assert.NotEmpty(rules);
        Assert.True(rules.Any(r => r.FileSystemRights.HasFlag(FileSystemRights.FullControl)),
            "SYSTEM must have FullControl in allow mode");
    }

    [Fact]
    public void ApplyAllowAcl_Directory_ProtectsInheritanceAndAddsAllowAces()
    {
        // Arrange
        var app = CreateAllowApp([new AllowAclEntry { Sid = SystemSid }]);
        var dir = CreateTempSubDir("protected-dir");

        // Act
        var changed = _allowService.ApplyAllowAcl(app, dir);

        // Assert
        Assert.True(changed);
        var security = new DirectoryInfo(dir).GetAccessControl();
        Assert.True(security.AreAccessRulesProtected, "Expected inheritance to be broken");
        var rules = GetExplicitAllowRules(security, SystemSid);
        Assert.NotEmpty(rules);
    }

    [Theory]
    // WorldSid is not in the hardcoded SYSTEM/Admins rules, so its rights come solely
    // from the entry-flag processing, making this Theory sensitive to individual flag values.
    [InlineData(true, false, FileSystemRights.ExecuteFile, true)]   // AllowExecute=true  → ExecuteFile granted
    [InlineData(false, false, FileSystemRights.ExecuteFile, false)]  // AllowExecute=false → ExecuteFile absent
    [InlineData(false, true, FileSystemRights.WriteData, true)]      // AllowWrite=true    → WriteData granted
    [InlineData(false, false, FileSystemRights.WriteData, false)]    // AllowWrite=false   → WriteData absent
    public void ApplyAllowAcl_EntryFlags_GrantOrDenyRightForWorldSid(
        bool allowExecute, bool allowWrite, FileSystemRights rightToCheck, bool shouldHave)
    {
        // Arrange
        var app = CreateAllowApp([new AllowAclEntry { Sid = WorldSid, AllowExecute = allowExecute, AllowWrite = allowWrite }]);
        var filePath = CreateTempFile($"flags-{allowExecute}-{allowWrite}.exe");

        // Act
        _allowService.ApplyAllowAcl(app, filePath);

        // Assert: Everyone must have the ACE (at minimum Read), and the specific right must be present/absent
        var security = new FileInfo(filePath).GetAccessControl();
        var rules = GetExplicitAllowRules(security, WorldSid);
        Assert.NotEmpty(rules);
        var hasRight = rules.Any(r => r.FileSystemRights.HasFlag(rightToCheck));
        Assert.Equal(shouldHave, hasRight);
    }

    [Fact]
    public void RevertAllowAcl_RestoredInheritanceAndRemovesAces()
    {
        // Arrange: first apply, then revert
        var app = CreateAllowApp([new AllowAclEntry { Sid = SystemSid }]);
        var filePath = CreateTempFile("to-revert.exe");
        _allowService.ApplyAllowAcl(app, filePath);

        // Verify the ACEs were present before revert
        Assert.NotEmpty(GetExplicitAllowRules(new FileInfo(filePath).GetAccessControl(), SystemSid));

        // Act
        _allowService.RevertAllowAcl(filePath, app);

        // Assert: inheritance restored and explicit ACEs removed
        var security = new FileInfo(filePath).GetAccessControl();
        Assert.False(security.AreAccessRulesProtected,
            "Expected inheritance to be restored after revert");
        Assert.Empty(GetExplicitAllowRules(security, SystemSid));
    }

    [Fact]
    public void CleanupAllowModeAces_PathDoesNotExist_NoErrors()
    {
        var nonExistent = Path.Combine(_tempDir.Path, "ghost.exe");

        var ex = Record.Exception(() => _allowService.CleanupAllowModeAces(nonExistent, false));

        Assert.Null(ex);
    }

    [Fact]
    public void CleanupAllowModeAces_UnprotectedPath_NoChange()
    {
        // A file with inherited (unprotected) ACL — cleanup should skip it
        var filePath = CreateTempFile("unprotected.exe");

        _allowService.CleanupAllowModeAces(filePath, false);

        _log.Verify(l => l.Info(It.Is<string>(s => s.Contains("Cleaned up allow-mode"))), Times.Never);
    }

    [Fact]
    public void CleanupAllowModeAces_ProtectedPath_RemovesExplicitAllowAcesAndRestoresInheritance()
    {
        // Arrange: apply allow mode first, which breaks inheritance
        var app = CreateAllowApp([new AllowAclEntry { Sid = SystemSid }]);
        var filePath = CreateTempFile("cleanup-target.exe");
        _allowService.ApplyAllowAcl(app, filePath);
        Assert.True(new FileInfo(filePath).GetAccessControl().AreAccessRulesProtected);

        // Act
        _allowService.CleanupAllowModeAces(filePath, false);

        // Assert: inheritance restored, explicit allow ACEs removed, and cleanup logged
        var security = new FileInfo(filePath).GetAccessControl();
        Assert.False(security.AreAccessRulesProtected,
            "Expected inheritance to be restored after cleanup");
        Assert.Empty(GetExplicitAllowRules(security, SystemSid));
        _log.Verify(l => l.Info(It.Is<string>(s => s.Contains("Cleaned up allow-mode"))), Times.Once);
    }

    // ── AclDenyModeService ───────────────────────────────────────────────────

    [Fact]
    public void ApplyDeny_NonExistentPath_ReturnsFalse()
    {
        // Arrange
        var path = Path.Combine(_tempDir.Path, "ghost.exe");
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Act
        var changed = _denyService.ApplyDeny(path, false, allowed, DeniedRights.Execute);

        // Assert
        Assert.False(changed);
    }

    [Fact]
    public void ApplyDeny_EmptyLocalUsers_ReturnsFalse()
    {
        // Arrange: no local users (default mock) → nothing to deny
        var filePath = CreateTempFile("nodeny.exe");

        // Act
        var changed = _denyService.ApplyDeny(filePath, false, [], DeniedRights.Execute);

        // Assert: no users to apply deny rules to
        Assert.False(changed);
    }

    [Fact]
    public void ApplyDeny_LocalUserNotInAllowedSet_AddsDenyAce()
    {
        // Arrange: one local user (using Everyone/World SID to guarantee it's valid and addable)
        _localUserProvider.Setup(p => p.GetLocalUserAccounts())
            .Returns([new LocalUserAccount("Everyone", WorldSid)]);
        var filePath = CreateTempFile("deny-target.exe");
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // empty → deny all

        // Act
        var changed = _denyService.ApplyDeny(filePath, false, allowed, DeniedRights.Execute);

        // Assert: deny ACE applied for Everyone
        Assert.True(changed);
        var security = new FileInfo(filePath).GetAccessControl();
        var denyRules = GetExplicitDenyRules(security, WorldSid);
        Assert.NotEmpty(denyRules);
        Assert.True(denyRules.Any(r => r.FileSystemRights.HasFlag(FileSystemRights.ExecuteFile)),
            "Expected ExecuteFile deny ACE for Everyone");
    }

    [Fact]
    public void ApplyDeny_LocalUserInAllowedSet_SkipsDenyForThatUser()
    {
        // Arrange: user is in the allowed set → must NOT receive a deny ACE
        _localUserProvider.Setup(p => p.GetLocalUserAccounts())
            .Returns([new LocalUserAccount("Everyone", WorldSid)]);
        var filePath = CreateTempFile("skip-deny.exe");
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { WorldSid };

        // Act
        var changed = _denyService.ApplyDeny(filePath, false, allowed, DeniedRights.Execute);

        // Assert: nothing was changed, and no deny ACE exists for Everyone (it's in the allow set)
        Assert.False(changed);
        var security = new FileInfo(filePath).GetAccessControl();
        var denyRules = GetExplicitDenyRules(security, WorldSid);
        Assert.Empty(denyRules);
    }

    [Fact]
    public void ApplyDeny_DeniedRightsExecuteWrite_GrantsCorrectMask()
    {
        // Arrange
        _localUserProvider.Setup(p => p.GetLocalUserAccounts())
            .Returns([new LocalUserAccount("Everyone", WorldSid)]);
        var filePath = CreateTempFile("exwrite-deny.exe");

        // Act
        _denyService.ApplyDeny(filePath, false, [], DeniedRights.ExecuteWrite);

        // Assert: ACE must include both ExecuteFile and WriteData
        var security = new FileInfo(filePath).GetAccessControl();
        var denyRules = GetExplicitDenyRules(security, WorldSid);
        Assert.NotEmpty(denyRules);
        var combinedRights = denyRules.Aggregate(
            (FileSystemRights)0, (acc, r) => acc | r.FileSystemRights);
        Assert.True(combinedRights.HasFlag(FileSystemRights.ExecuteFile),
            "ExecuteFile must be in ExecuteWrite deny mask");
        Assert.True(combinedRights.HasFlag(FileSystemRights.WriteData),
            "WriteData must be in ExecuteWrite deny mask");
    }

    [Fact]
    public void RemoveManagedDenyAces_AfterApplyDeny_RemovesDenyAces()
    {
        // Arrange: apply deny first, then remove
        _localUserProvider.Setup(p => p.GetLocalUserAccounts())
            .Returns([new LocalUserAccount("Everyone", WorldSid)]);
        var filePath = CreateTempFile("remove-deny.exe");
        _denyService.ApplyDeny(filePath, false, [], DeniedRights.Execute);

        // Verify deny ACE was added
        Assert.NotEmpty(GetExplicitDenyRules(new FileInfo(filePath).GetAccessControl(), WorldSid));

        // Act
        _denyService.RemoveManagedDenyAces(filePath, false);

        // Assert: deny ACE removed
        var security = new FileInfo(filePath).GetAccessControl();
        var remaining = GetExplicitDenyRules(security, WorldSid);
        Assert.Empty(remaining);
    }

    [Fact]
    public void RemoveManagedDenyAces_NonExistentPath_NoErrors()
    {
        var nonExistent = Path.Combine(_tempDir.Path, "ghost.exe");

        var ex = Record.Exception(() => _denyService.RemoveManagedDenyAces(nonExistent, false));

        Assert.Null(ex);
    }

    [Fact]
    public void ApplyDenyToFolderPerSid_EmptyInput_ReturnsFalse()
    {
        var dir = CreateTempSubDir("empty-deny-dir");

        var changed = _denyService.ApplyDenyToFolderPerSid(dir, new Dictionary<string, DeniedRights>());

        Assert.False(changed);
    }

    [Fact]
    public void ApplyDenyToFolderPerSid_WithEntry_AddsDenyAceForSid()
    {
        // Arrange
        var dir = CreateTempSubDir("per-sid-deny-dir");
        var perSid = new Dictionary<string, DeniedRights>(StringComparer.OrdinalIgnoreCase)
        {
            [WorldSid] = DeniedRights.Execute
        };

        // Act
        var changed = _denyService.ApplyDenyToFolderPerSid(dir, perSid);

        // Assert
        Assert.True(changed);
        var security = new DirectoryInfo(dir).GetAccessControl();
        var denyRules = GetExplicitDenyRules(security, WorldSid);
        Assert.NotEmpty(denyRules);
        Assert.True(denyRules.Any(r => r.FileSystemRights.HasFlag(FileSystemRights.ExecuteFile)),
            "Expected ExecuteFile deny ACE");
    }

    [Fact]
    public void ApplyDenyToFolderPerSid_DoesNotRemoveExternalDenyAce()
    {
        // Arrange: pre-place an external deny ACE with bits OUTSIDE ManagedDenyRightsMask.
        // ManagedDenyRightsMask covers Execute, Write (WriteData/AppendData/WriteAttributes/
        // WriteExtendedAttributes), Delete, DeleteSubdirectoriesAndFiles, and Read variants.
        // TakeOwnership (WriteOwner) is NOT in the mask — using it makes this ACE external.
        // The external ACE uses: TakeOwnership | ExecuteFile — ExecuteFile is inside the mask,
        // but TakeOwnership is outside. Because (TakeOwnership | ExecuteFile) & ~mask != 0,
        // the subset predicate must NOT classify this ACE as managed and must leave it untouched.
        var dir = CreateTempSubDir("external-deny-dir");

        // Place an external deny ACE for WorldSid that includes TakeOwnership (outside the mask).
        var externalRights = FileSystemRights.TakeOwnership | FileSystemRights.ExecuteFile;
        var security = new DirectoryInfo(dir).GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WorldSid),
            externalRights,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Deny));
        new DirectoryInfo(dir).SetAccessControl(security);

        // Also register WorldSid as a "local user" so it appears in the managed SID set
        _localUserProvider.Setup(p => p.GetLocalUserAccounts())
            .Returns([new LocalUserAccount("Everyone", WorldSid)]);

        // Apply deny mode for a different SID (SystemSid) — no entry for WorldSid in desired rules
        var perSid = new Dictionary<string, DeniedRights>(StringComparer.OrdinalIgnoreCase)
        {
            [SystemSid] = DeniedRights.Execute
        };

        // Act: ApplyDenyToFolderPerSid — must NOT remove the external WorldSid deny ACE
        _denyService.ApplyDenyToFolderPerSid(dir, perSid);

        // Assert: external deny ACE for WorldSid must still be present (TakeOwnership bit survived)
        var afterSecurity = new DirectoryInfo(dir).GetAccessControl();
        var remainingDenyRules = GetExplicitDenyRules(afterSecurity, WorldSid);
        Assert.NotEmpty(remainingDenyRules);
        var combinedRights = remainingDenyRules.Aggregate(
            (FileSystemRights)0, (acc, r) => acc | r.FileSystemRights);
        Assert.True(combinedRights.HasFlag(FileSystemRights.TakeOwnership),
            "External deny ACE with TakeOwnership bit must NOT be removed — TakeOwnership is outside ManagedDenyRightsMask");
    }

    [Fact]
    public void ApplyDenyToFolderPerSid_RemovesManagedDenyAce_WhenNotInDesiredSet()
    {
        // Arrange: a previously-managed deny ACE for WorldSid (rights are a pure subset of ManagedDenyRightsMask)
        // is placed on the directory. After applying deny mode with an empty desired set,
        // the managed ACE must be removed (because it is a subset of ManagedDenyRightsMask).
        var dir = CreateTempSubDir("managed-cleanup-dir");

        // ManagedDenyRightsMask = Execute | ExecuteWrite | ExecuteReadWrite combined.
        // ExecuteFile alone is a strict subset of the mask → classified as managed → removed.
        var managedRights = FileSystemRights.ExecuteFile;
        var security = new DirectoryInfo(dir).GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WorldSid),
            managedRights,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Deny));
        new DirectoryInfo(dir).SetAccessControl(security);

        _localUserProvider.Setup(p => p.GetLocalUserAccounts())
            .Returns([new LocalUserAccount("Everyone", WorldSid)]);

        // Act: empty desired set → no deny ACEs desired → managed ACE should be removed
        _denyService.ApplyDenyToFolderPerSid(dir, new Dictionary<string, DeniedRights>());

        // Assert: managed deny ACE removed
        var afterSecurity = new DirectoryInfo(dir).GetAccessControl();
        var remaining = GetExplicitDenyRules(afterSecurity, WorldSid);
        Assert.Empty(remaining);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private string CreateTempFile(string name)
    {
        var path = Path.Combine(_tempDir.Path, name);
        File.WriteAllBytes(path, []);
        return path;
    }

    private string CreateTempSubDir(string name)
    {
        var path = Path.Combine(_tempDir.Path, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static AppEntry CreateAllowApp(List<AllowAclEntry>? entries) => new()
    {
        Id = "test-allow-01",
        Name = "TestAllowApp",
        ExePath = @"C:\test.exe",
        AclMode = AclMode.Allow,
        RestrictAcl = true,
        AllowedAclEntries = entries
    };

    private static List<FileSystemAccessRule> GetExplicitAllowRules(FileSystemSecurity security, string sid)
    {
        var target = new SecurityIdentifier(sid);
        var rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier));
        return rules.Cast<FileSystemAccessRule>()
            .Where(r => r.AccessControlType == AccessControlType.Allow
                && r.IdentityReference is SecurityIdentifier s && s.Equals(target))
            .ToList();
    }

    private static List<FileSystemAccessRule> GetExplicitDenyRules(FileSystemSecurity security, string sid)
    {
        var target = new SecurityIdentifier(sid);
        var rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier));
        return rules.Cast<FileSystemAccessRule>()
            .Where(r => r.AccessControlType == AccessControlType.Deny
                && r.IdentityReference is SecurityIdentifier s && s.Equals(target))
            .ToList();
    }
}

