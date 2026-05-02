using System.Security.AccessControl;
using Moq;
using RunFence.Acl.Permissions;
using RunFence.Ipc;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Tests for <see cref="DirectoryValidator"/> including integration tests for
/// <c>ParseReparseSubstituteName</c> (private static), exercised through
/// <see cref="DirectoryValidator.ValidateAndHold"/>.
///
/// Junction points (IO_REPARSE_TAG_MOUNT_POINT) do not require elevation — they can be
/// created and cleaned up by normal non-admin users in their own directories.
/// Symbolic links typically require Developer Mode or SeCreateSymbolicLinkPrivilege, so
/// symlink cases are tested via mount-point junctions only.
/// </summary>
public class DirectoryValidatorTests : IDisposable
{
    private readonly Mock<IAclPermissionService> _aclPermission = new();
    private readonly DirectoryValidator _validator;
    private readonly TempDirectory _tempDir = new("DirectoryValidator_Test");

    // A fictitious but syntactically valid SID for testing
    private const string CallerSid = "S-1-5-21-9000000000-9000000000-9000000000-1001";

    public DirectoryValidatorTests()
    {
        _validator = new DirectoryValidator(_aclPermission.Object);
        // Default: caller has access (NeedsPermissionGrant returns false)
        _aclPermission.Setup(a => a.NeedsPermissionGrant(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<FileSystemRights>())).Returns(false);
    }

    public void Dispose() => _tempDir.Dispose();

    // ── Pre-validation guards ────────────────────────────────────────────────

    [Fact]
    public void ValidateAndHold_RelativePath_ReturnsInvalid()
    {
        using var handle = _validator.ValidateAndHold("relative\\path", CallerSid);

        Assert.False(handle.IsValid);
        Assert.NotNull(handle.Error);
    }

    [Fact]
    public void ValidateAndHold_UncPath_ReturnsInvalid()
    {
        using var handle = _validator.ValidateAndHold(@"\\server\share\folder", CallerSid);

        Assert.False(handle.IsValid);
        Assert.NotNull(handle.Error);
        Assert.Contains("UNC", handle.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateAndHold_NonExistentPath_ReturnsInvalid()
    {
        var nonExistent = Path.Combine(_tempDir.Path, "does-not-exist-" + Guid.NewGuid().ToString("N"));

        using var handle = _validator.ValidateAndHold(nonExistent, CallerSid);

        Assert.False(handle.IsValid);
    }

    [Fact]
    public void ValidateAndHold_FilePath_ReturnsInvalid()
    {
        // A file is not a directory — should fail the file-attribute check
        var filePath = Path.Combine(_tempDir.Path, "test.txt");
        File.WriteAllText(filePath, "content");

        using var handle = _validator.ValidateAndHold(filePath, CallerSid);

        Assert.False(handle.IsValid);
        Assert.NotNull(handle.Error);
    }

    // ── Regular directory ────────────────────────────────────────────────────

    [Fact]
    public void ValidateAndHold_RealDirectory_ReturnsValidWithCanonicalPath()
    {
        // Arrange: a plain, non-reparse directory
        var dir = Path.Combine(_tempDir.Path, "plain");
        Directory.CreateDirectory(dir);

        // Act
        using var handle = _validator.ValidateAndHold(dir, CallerSid);

        // Assert
        Assert.True(handle.IsValid);
        Assert.NotNull(handle.CanonicalPath);
        Assert.True(Path.IsPathFullyQualified(handle.CanonicalPath));
        Assert.EndsWith("plain", handle.CanonicalPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateAndHold_RealDirectory_NeedsPermissionGrantChecked()
    {
        // Arrange
        var dir = Path.Combine(_tempDir.Path, "permcheck");
        Directory.CreateDirectory(dir);

        // Act
        using var handle = _validator.ValidateAndHold(dir, CallerSid);

        // Assert: NeedsPermissionGrant is always called with the canonical path and caller SID
        _aclPermission.Verify(a => a.NeedsPermissionGrant(
            It.IsAny<string>(), CallerSid, FileSystemRights.ListDirectory), Times.Once);
    }

    [Fact]
    public void ValidateAndHold_CallerLacksPermission_ReturnsInvalid()
    {
        // Arrange: simulate access denied for this caller
        var dir = Path.Combine(_tempDir.Path, "noaccess");
        Directory.CreateDirectory(dir);
        _aclPermission.Setup(a => a.NeedsPermissionGrant(It.IsAny<string>(), CallerSid,
            FileSystemRights.ListDirectory)).Returns(true);

        // Act
        using var handle = _validator.ValidateAndHold(dir, CallerSid);

        // Assert
        Assert.False(handle.IsValid);
        Assert.NotNull(handle.Error);
    }

    // ── Junction (mount-point) reparse point tests ───────────────────────────
    //
    // These exercise ParseReparseSubstituteName (private) via the public ValidateAndHold path.
    // A junction with a valid local target: tag=IO_REPARSE_TAG_MOUNT_POINT, \??\ prefix stripped
    // → ValidateAndHold resolves to the target directory and validates that.

    [Fact]
    public void ValidateAndHold_JunctionToLocalDir_ResolvesAndValidatesTarget()
    {
        // Arrange: target is a real directory; junction points to it
        var target = Path.Combine(_tempDir.Path, "junction-target");
        Directory.CreateDirectory(target);
        var junction = Path.Combine(_tempDir.Path, "junction-link");
        JunctionHelper.CreateJunction(junction, target);

        try
        {
            // Act: validator should follow the junction and end up at target
            using var handle = _validator.ValidateAndHold(junction, CallerSid);

            // Assert: valid, and canonical path resolves to the real target (not the junction path)
            Assert.True(handle.IsValid, $"Expected valid handle, error: {handle.Error}");
            Assert.NotNull(handle.CanonicalPath);
            Assert.EndsWith("junction-target", handle.CanonicalPath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            // Clean up junction (not the target; TempDirectory will handle target)
            RemoveJunction(junction);
        }
    }

    [Fact]
    public void ValidateAndHold_JunctionToNonExistentTarget_ReturnsInvalid()
    {
        // Arrange: junction points to a path that does not exist
        var nonExistentTarget = Path.Combine(_tempDir.Path, "ghost-target-" + Guid.NewGuid().ToString("N"));
        var junction = Path.Combine(_tempDir.Path, "dead-junction");
        JunctionHelper.CreateJunction(junction, nonExistentTarget);

        try
        {
            // Act
            using var handle = _validator.ValidateAndHold(junction, CallerSid);

            // Assert: target doesn't exist → invalid
            Assert.False(handle.IsValid);
        }
        finally
        {
            RemoveJunction(junction);
        }
    }

    [Fact]
    public void ValidateAndHold_JunctionToUncTarget_ReturnsInvalid()
    {
        // Arrange: build a raw junction whose substitute name is a UNC path (\\server\share).
        // GetFileAttributes follows the reparse point and will fail when the UNC target is
        // unreachable, so validation fails at the GetFileAttributes step (before reaching
        // ParseReparseSubstituteName). Either way, the result must be invalid — the UNC path
        // is rejected at some layer of the validation pipeline.
        var junction = Path.Combine(_tempDir.Path, "unc-junction");
        JunctionHelper.CreateJunctionWithRawSubstituteName(junction, @"\\server\share");

        try
        {
            // Act
            using var handle = _validator.ValidateAndHold(junction, CallerSid);

            // Assert: any unreachable or UNC reparse target is invalid
            Assert.False(handle.IsValid);
            Assert.NotNull(handle.Error);
        }
        finally
        {
            RemoveJunction(junction);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void RemoveJunction(string junctionPath)
    {
        try
        {
            if (Directory.Exists(junctionPath))
                Directory.Delete(junctionPath);
        }
        catch
        {
        }
    }
}
