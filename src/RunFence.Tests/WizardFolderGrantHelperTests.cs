using Moq;
using RunFence.Acl;
using RunFence.Acl.QuickAccess;
using RunFence.Core.Models;
using RunFence.Wizard;
using Xunit;

namespace RunFence.Tests;

public class WizardFolderGrantHelperTests
{
    private const string Sid = "S-1-5-21-1234567890-1234567890-1234567890-1001";

    private readonly Mock<IPathGrantService> _pathGrantService = new();
    private readonly Mock<IQuickAccessPinService> _quickAccessPinService = new();
    private readonly Mock<IWizardProgressReporter> _progress = new();

    private WizardFolderGrantHelper CreateHelper() =>
        new(_pathGrantService.Object, _quickAccessPinService.Object);

    // --- Empty path skipping ---

    [Fact]
    public async Task GrantFolderAccessAsync_SkipsEmptyPaths_NoInteractions()
    {
        // Arrange: null/empty strings are skipped by string.IsNullOrEmpty check
        var paths = new[] { "" };
        var rights = SavedRightsState.DefaultForMode(isDeny: false);

        var helper = CreateHelper();

        // Act
        await helper.GrantFolderAccessAsync(paths, Sid, rights, _progress.Object);

        // Assert: no grant or pin interaction and no status progress
        _pathGrantService.VerifyNoOtherCalls();
        _quickAccessPinService.VerifyNoOtherCalls();
        _progress.VerifyNoOtherCalls();
    }

    // --- Error reporting ---

    [Fact]
    public async Task GrantFolderAccessAsync_ReportsErrorForFailedPath_ContinuesToNextPath()
    {
        // Arrange: two paths; first throws, second succeeds on a real temp directory.
        using var dir = new TempDirectory("RunFenceTest");
        var tempDir = dir.Path;
        const string failingPath = @"C:\NonExistentPath\WillThrow";
        var rights = SavedRightsState.DefaultForMode(isDeny: false);
        _pathGrantService
            .Setup(s => s.EnsureAccess(Sid, failingPath, rights, null, false))
            .Throws(new GrantOperationException(
                GrantApplyFailureStep.GrantAclApply,
                failingPath,
                null,
                new UnauthorizedAccessException("Access denied")));
        _pathGrantService
            .Setup(s => s.EnsureAccess(Sid, tempDir, rights, null, false))
            .Returns(new GrantApplyResult(GrantApplied: true, DatabaseModified: true, DurableSaveCompleted: true));

        var helper = CreateHelper();

        // Act
        await helper.GrantFolderAccessAsync([failingPath, tempDir], Sid, rights, _progress.Object);

        // Assert: error reported for failing path and status for the succeeding path
        var expectedStatus = $"Granting access to {Path.GetFileName(tempDir)}...";
        _progress.Verify(
            p => p.ReportError(It.Is<string>(s =>
                s.Contains("C:\\NonExistentPath\\WillThrow") &&
                s.Contains("Access denied"))),
            Times.Once);
        _progress.Verify(p => p.ReportStatus(expectedStatus), Times.Once);
    }

    // --- Pinning accessible folders ---

    [Fact]
    public async Task GrantFolderAccessAsync_PinsExistingDirectory_WhenGrantAdded()
    {
        // Arrange: a real temp directory that exists on disk; grant returns GrantAdded=true.
        using var dir = new TempDirectory("RunFenceTest");
        var tempDir = dir.Path;
        var rights = SavedRightsState.DefaultForMode(isDeny: false);
        _pathGrantService
            .Setup(s => s.EnsureAccess(Sid, tempDir, rights, null, false))
            .Returns(new GrantApplyResult(GrantApplied: true, DatabaseModified: true, DurableSaveCompleted: true));

        List<string>? pinnedPaths = null;
        _quickAccessPinService
            .Setup(p => p.PinFolders(
                Sid,
                It.Is<IReadOnlyList<string>>(paths => paths.Count == 1 && paths[0] == tempDir)))
            .Callback<string, IReadOnlyList<string>>((_, paths) => pinnedPaths = [..paths]);

        var helper = CreateHelper();

        // Act
        await helper.GrantFolderAccessAsync([tempDir], Sid, rights, _progress.Object);

        // Assert: PinFolders called once with normalized existing directory
        _quickAccessPinService.Verify(p => p.PinFolders(Sid, It.Is<IReadOnlyList<string>>(paths => paths.Count == 1 && paths[0] == tempDir)), Times.Once);
        Assert.NotNull(pinnedPaths);
        Assert.Single(pinnedPaths);
        Assert.True(Directory.Exists(pinnedPaths[0]));
        Assert.Equal(Path.GetFullPath(pinnedPaths[0]), pinnedPaths[0]);
    }

    [Fact]
    public async Task GrantFolderAccessAsync_DoesNotPin_WhenGrantAlreadyExisted()
    {
        // Arrange: grant returns GrantAdded=false (access already granted)
        using var dir = new TempDirectory("RunFenceTest");
        var tempDir = dir.Path;
        var rights = SavedRightsState.DefaultForMode(isDeny: false);
        _pathGrantService
            .Setup(s => s.EnsureAccess(Sid, tempDir, rights, null, false))
            .Returns(default(GrantApplyResult));

        var helper = CreateHelper();

        // Act
        await helper.GrantFolderAccessAsync([tempDir], Sid, rights, _progress.Object);

        // Assert: PinFolders never called
        _quickAccessPinService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GrantFolderAccessAsync_DoesNotPin_WhenPathIsNotDirectory()
    {
        // Arrange: EnsureAccess returns GrantAdded=true but path is a file (not a directory)
        var tempFile = Path.GetTempFileName();
        var rights = SavedRightsState.DefaultForMode(isDeny: false);
        try
        {
            _pathGrantService
                .Setup(s => s.EnsureAccess(Sid, tempFile, rights, null, false))
                .Returns(new GrantApplyResult(GrantApplied: true, DatabaseModified: true, DurableSaveCompleted: true));

            var helper = CreateHelper();

            // Act
            await helper.GrantFolderAccessAsync([tempFile], Sid, rights, _progress.Object);

            // Assert: PinFolders not called because path is a file, not a directory
            _quickAccessPinService.VerifyNoOtherCalls();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
