using Moq;
using RunFence.Acl;
using RunFence.Acl.Permissions;
using RunFence.Core;
using RunFence.Core.Models;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Tests for <see cref="DriveAclReplacer"/> — verifies <see cref="IPermissionGrantService"/>
/// interactions and error handling without requiring elevation or real drive-root ACL access.
/// </summary>
public class DriveAclReplacerTests : IDisposable
{
    private const string TestSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";

    private readonly Mock<IPermissionGrantService> _permissionGrantService = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly DriveAclReplacer _replacer;
    private readonly TempDirectory _tempDir;

    public DriveAclReplacerTests()
    {
        _replacer = new DriveAclReplacer(_permissionGrantService.Object, _log.Object);
        _tempDir = new TempDirectory("DriveAclReplacer");
    }

    public void Dispose() => _tempDir.Dispose();

    // --- RecordGrantWithRights is always called on success ---

    [Fact]
    public void ReplaceDriveAcl_AccessibleDirectory_CallsRecordGrantWithRights()
    {
        // Use a real accessible directory so GetAccessControl succeeds.
        // The temp directory has no broad-access SID ACEs, so nothing is modified —
        // RecordGrantWithRights must still be called regardless.
        var savedRights = SavedRightsState.DefaultForMode(isDeny: false, own: false);

        _replacer.ReplaceDriveAcl(_tempDir.Path, TestSid, savedRights);

        _permissionGrantService.Verify(
            p => p.RecordGrantWithRights(_tempDir.Path, TestSid, savedRights),
            Times.Once);
    }

    [Fact]
    public void ReplaceDriveAcl_AccessibleDirectory_ReturnsNull()
    {
        var savedRights = SavedRightsState.DefaultForMode(isDeny: false, own: false);

        var result = _replacer.ReplaceDriveAcl(_tempDir.Path, TestSid, savedRights);

        Assert.Null(result);
    }

    // --- Error handling ---

    [Fact]
    public void ReplaceDriveAcl_NonExistentPath_ReturnsErrorMessage()
    {
        var nonExistent = Path.Combine(_tempDir.Path, "does_not_exist");
        var savedRights = SavedRightsState.DefaultForMode(isDeny: false, own: false);

        var result = _replacer.ReplaceDriveAcl(nonExistent, TestSid, savedRights);

        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    public void ReplaceDriveAcl_NonExistentPath_LogsError()
    {
        var nonExistent = Path.Combine(_tempDir.Path, "does_not_exist");
        var savedRights = SavedRightsState.DefaultForMode(isDeny: false, own: false);

        _replacer.ReplaceDriveAcl(nonExistent, TestSid, savedRights);

        _log.Verify(l => l.Error(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }

    [Fact]
    public void ReplaceDriveAcl_NonExistentPath_DoesNotCallRecordGrantWithRights()
    {
        var nonExistent = Path.Combine(_tempDir.Path, "does_not_exist");
        var savedRights = SavedRightsState.DefaultForMode(isDeny: false, own: false);

        _replacer.ReplaceDriveAcl(nonExistent, TestSid, savedRights);

        _permissionGrantService.Verify(
            p => p.RecordGrantWithRights(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SavedRightsState>()),
            Times.Never);
    }
}