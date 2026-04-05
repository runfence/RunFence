using System.Security.AccessControl;
using Moq;
using RunFence.Acl;
using RunFence.Core;
using RunFence.Core.Models;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Tests for <see cref="GrantedPathAclService"/> covering non-elevated logic:
/// path status checks, ACL apply/revert on user-owned temp files, batch revert filtering,
/// and rights mask constants.
/// Tests run as a non-elevated user — all filesystem operations use temp directories
/// owned by the test user, so no elevation is required.
/// </summary>
public class GrantedPathAclServiceTests : IDisposable
{
    // A fake (non-existent) SID used for ACE manipulation; the SID need not resolve to a real account
    // for NTFS ACE operations — Windows accepts any syntactically valid SID.
    private const string TestSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";

    private readonly Mock<ILoggingService> _log = new();
    private readonly GrantedPathAclService _service;
    private readonly TempDirectory _tempDir;

    public GrantedPathAclServiceTests()
    {
        _service = new GrantedPathAclService(_log.Object);
        _tempDir = new TempDirectory("GrantedPathAcl");
    }

    public void Dispose() => _tempDir.Dispose();

    // --- Rights mask constants ---

    [Fact]
    public void ReadRightsMask_IncludesSynchronize()
    {
        Assert.True((GrantedPathAclService.ReadRightsMask & FileSystemRights.Synchronize) != 0);
    }

    [Fact]
    public void ReadRightsMask_IncludesReadData()
    {
        Assert.True((GrantedPathAclService.ReadRightsMask & FileSystemRights.ReadData) != 0);
    }

    [Fact]
    public void WriteRightsMask_IncludesDeleteSubdirectoriesAndFiles()
    {
        Assert.True((GrantedPathAclService.WriteRightsMask & FileSystemRights.DeleteSubdirectoriesAndFiles) != 0);
    }

    [Fact]
    public void SpecialRightsMask_IncludesChangePermissionsAndTakeOwnership()
    {
        Assert.True((GrantedPathAclService.SpecialRightsMask & FileSystemRights.ChangePermissions) != 0);
        Assert.True((GrantedPathAclService.SpecialRightsMask & FileSystemRights.TakeOwnership) != 0);
    }

    // --- CheckPathStatus ---

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CheckPathStatus_MissingPath_ReturnsUnavailable(bool isDeny)
    {
        var nonExistent = Path.Combine(_tempDir.Path, "does_not_exist.exe");

        var status = _service.CheckPathStatus(nonExistent, TestSid, isDeny);

        Assert.Equal(PathAclStatus.Unavailable, status);
    }

    [Fact]
    public void CheckPathStatus_ExistingPathNoAce_ReturnsBroken()
    {
        var file = Path.Combine(_tempDir.Path, "test.txt");
        File.WriteAllText(file, "test");

        // No explicit ACE for our fake SID → Broken
        var status = _service.CheckPathStatus(file, TestSid, isDeny: false);

        Assert.Equal(PathAclStatus.Broken, status);
    }

    // --- ApplyAllowRights / RevertGrant ---

    [Fact]
    public void ApplyReadOnlyGrant_ThenCheckStatus_ReturnsAvailable()
    {
        var file = Path.Combine(_tempDir.Path, "apply_test.txt");
        File.WriteAllText(file, "test");

        _service.ApplyReadOnlyGrant(file, TestSid);

        Assert.Equal(PathAclStatus.Available, _service.CheckPathStatus(file, TestSid, isDeny: false));
    }

    [Fact]
    public void ApplyAllowRights_ThenCheckStatus_ReturnsAvailable()
    {
        var file = Path.Combine(_tempDir.Path, "allow_test.txt");
        File.WriteAllText(file, "test");

        _service.ApplyAllowRights(file, TestSid, new AllowRights(Execute: false, Write: false, Special: false));

        Assert.Equal(PathAclStatus.Available, _service.CheckPathStatus(file, TestSid, isDeny: false));
    }

    [Fact]
    public void ApplyDenyRights_ThenCheckStatus_ReturnsAvailable()
    {
        var file = Path.Combine(_tempDir.Path, "deny_test.txt");
        File.WriteAllText(file, "test");

        _service.ApplyDenyRights(file, TestSid, new DenyRights(Read: false, Execute: false));

        Assert.Equal(PathAclStatus.Available, _service.CheckPathStatus(file, TestSid, isDeny: true));
    }

    [Fact]
    public void RevertGrant_AfterApplyAllow_CheckStatusReturnsBroken()
    {
        var file = Path.Combine(_tempDir.Path, "revert_allow.txt");
        File.WriteAllText(file, "test");
        _service.ApplyReadOnlyGrant(file, TestSid);

        _service.RevertGrant(file, TestSid, isDeny: false);

        Assert.Equal(PathAclStatus.Broken, _service.CheckPathStatus(file, TestSid, isDeny: false));
    }

    [Fact]
    public void RevertGrant_AfterApplyDeny_CheckStatusReturnsBroken()
    {
        var file = Path.Combine(_tempDir.Path, "revert_deny.txt");
        File.WriteAllText(file, "test");
        _service.ApplyDenyRights(file, TestSid, new DenyRights(Read: false, Execute: false));

        _service.RevertGrant(file, TestSid, isDeny: true);

        Assert.Equal(PathAclStatus.Broken, _service.CheckPathStatus(file, TestSid, isDeny: true));
    }

    [Fact]
    public void RevertAllGrants_AfterApplyBoth_CheckStatusReturnsBroken()
    {
        var file = Path.Combine(_tempDir.Path, "revert_all.txt");
        File.WriteAllText(file, "test");
        _service.ApplyReadOnlyGrant(file, TestSid);
        _service.ApplyDenyRights(file, TestSid, new DenyRights(Read: false, Execute: false));

        _service.RevertAllGrants(file, TestSid);

        Assert.Equal(PathAclStatus.Broken, _service.CheckPathStatus(file, TestSid, isDeny: false));
        Assert.Equal(PathAclStatus.Broken, _service.CheckPathStatus(file, TestSid, isDeny: true));
    }

    // --- RevertAllGrantsBatch ---

    [Fact]
    public void RevertAllGrantsBatch_EmptyList_DoesNothing()
    {
        // Should complete without error or logging
        _service.RevertAllGrantsBatch([], TestSid);

        _log.Verify(l => l.Warn(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void RevertAllGrantsBatch_OnlyTraverseOnlyEntries_SkipsAll()
    {
        var grants = new List<GrantedPathEntry>
        {
            new() { Path = @"C:\SomePath", IsTraverseOnly = true },
            new() { Path = @"C:\OtherPath", IsTraverseOnly = true }
        };

        // Paths don't exist — if they were processed, NativeAclAccessor would throw or log.
        // IsTraverseOnly entries must be skipped entirely: no exception, no warning.
        _service.RevertAllGrantsBatch(grants, TestSid);

        _log.Verify(l => l.Warn(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void RevertAllGrantsBatch_MixedEntries_SkipsTraverseOnlyAndHandlesNonExistentGracefully()
    {
        var nonExistentPath = Path.Combine(_tempDir.Path, "missing_file.exe");
        var grants = new List<GrantedPathEntry>
        {
            new() { Path = nonExistentPath, IsTraverseOnly = false },
            new() { Path = @"C:\TraverseOnly", IsTraverseOnly = true }
        };

        // Non-traverse entry on non-existent path: RemoveExplicitAces silently skips missing files
        // (NativeAclAccessor only processes paths where File.Exists or Directory.Exists is true).
        // Traverse-only entry is filtered out entirely before NativeAclAccessor is reached.
        _service.RevertAllGrantsBatch(grants, TestSid);

        _log.Verify(l => l.Warn(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void RevertAllGrantsBatch_NonTraverseEntry_RemovesAce()
    {
        var file = Path.Combine(_tempDir.Path, "batch_revert.txt");
        File.WriteAllText(file, "test");
        _service.ApplyReadOnlyGrant(file, TestSid);
        Assert.Equal(PathAclStatus.Available, _service.CheckPathStatus(file, TestSid, isDeny: false));

        _service.RevertAllGrantsBatch([new GrantedPathEntry { Path = file, IsTraverseOnly = false }], TestSid);

        Assert.Equal(PathAclStatus.Broken, _service.CheckPathStatus(file, TestSid, isDeny: false));
    }
}