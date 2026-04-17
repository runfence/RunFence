using Moq;
using RunFence.Acl;
using RunFence.Core;
using RunFence.Core.Models;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Tests for <see cref="DriveAclReplacer"/> — verifies <see cref="IPathGrantService"/>
/// interactions and error handling without requiring elevation or real drive-root ACL access.
/// </summary>
public class DriveAclReplacerTests : IDisposable
{
    private const string TestSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";

    private readonly Mock<IPathGrantService> _pathGrantService = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly DriveAclReplacer _replacer;
    private readonly TempDirectory _tempDir;

    public DriveAclReplacerTests()
    {
        _replacer = new DriveAclReplacer(_pathGrantService.Object, _log.Object);
        _tempDir = new TempDirectory("DriveAclReplacer");
    }

    public void Dispose() => _tempDir.Dispose();

    // --- UpdateFromPath is always called on success ---

    [Fact]
    public void ReplaceDriveAcl_AccessibleDirectory_CallsUpdateFromPath()
    {
        // Use a real accessible directory so GetAccessControl succeeds.
        // The temp directory has no broad-access SID ACEs, so nothing is modified —
        // UpdateFromPath must still be called regardless.

        _replacer.ReplaceDriveAcl(_tempDir.Path, TestSid);

        _pathGrantService.Verify(
            p => p.UpdateFromPath(_tempDir.Path, TestSid),
            Times.Once);
    }

    [Fact]
    public void ReplaceDriveAcl_AccessibleDirectory_ReturnsNull()
    {
        var result = _replacer.ReplaceDriveAcl(_tempDir.Path, TestSid);

        Assert.Null(result);
    }

    // --- Error handling ---

    [Fact]
    public void ReplaceDriveAcl_NonExistentPath_ReturnsErrorLogsAndDoesNotCallUpdateFromPath()
    {
        var nonExistent = Path.Combine(_tempDir.Path, "does_not_exist");

        var result = _replacer.ReplaceDriveAcl(nonExistent, TestSid);

        Assert.NotNull(result);
        Assert.NotEmpty(result);
        _log.Verify(l => l.Error(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
        _pathGrantService.Verify(
            p => p.UpdateFromPath(It.IsAny<string>(), It.IsAny<string?>()),
            Times.Never);
    }
}
