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

        var helper = CreateHelper();

        // Act
        await helper.GrantFolderAccessAsync(paths, Sid, SavedRightsState.DefaultForMode(isDeny: false), _progress.Object);

        // Assert: EnsureAccess and PinFolders never called; no status reported
        _pathGrantService.Verify(
            s => s.EnsureAccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<SavedRightsState>(),
                It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()),
            Times.Never);
        _quickAccessPinService.Verify(
            p => p.PinFolders(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()),
            Times.Never);
        _progress.Verify(p => p.ReportStatus(It.IsAny<string>()), Times.Never);
    }

    // --- Error reporting ---

    [Fact]
    public async Task GrantFolderAccessAsync_ReportsErrorForFailedPath_ContinuesToNextPath()
    {
        // Arrange: two paths — first throws, second succeeds on a real temp directory.
        // Use It.Is to distinguish the two paths without relying on Path.GetFullPath normalization.
        using var dir = new TempDirectory("RunFenceTest");
        var tempDir = dir.Path;
        const string failingPath = @"C:\NonExistentPath\WillThrow";
        _pathGrantService
            .Setup(s => s.EnsureAccess(
                Sid, It.Is<string>(p => p.Equals(failingPath, StringComparison.OrdinalIgnoreCase)),
                It.IsAny<SavedRightsState>(),
                It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()))
            .Throws(new UnauthorizedAccessException("Access denied"));
        _pathGrantService
            .Setup(s => s.EnsureAccess(
                Sid, It.Is<string>(p => !p.Equals(failingPath, StringComparison.OrdinalIgnoreCase)),
                It.IsAny<SavedRightsState>(),
                It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()))
            .Returns(new GrantOperationResult(GrantAdded: true, TraverseAdded: false, DatabaseModified: true));

        var helper = CreateHelper();

        // Act
        await helper.GrantFolderAccessAsync([failingPath, tempDir], Sid, SavedRightsState.DefaultForMode(isDeny: false), _progress.Object);

        // Assert: error reported for failing path
        _progress.Verify(
            p => p.ReportError(It.Is<string>(s =>
                s.Contains("C:\\NonExistentPath\\WillThrow") &&
                s.Contains("Access denied"))),
            Times.Once);
        // Status reported for the succeeding path
        _progress.Verify(
            p => p.ReportStatus(It.Is<string>(s => s.Contains(Path.GetFileName(tempDir)))),
            Times.Once);
    }

    // --- Pinning accessible folders ---

    [Fact]
    public async Task GrantFolderAccessAsync_PinsExistingDirectory_WhenGrantAdded()
    {
        // Arrange: a real temp directory that exists on disk; grant returns GrantAdded=true.
        // Use It.IsAny for the path to avoid relying on Path.GetFullPath normalization details.
        using var dir = new TempDirectory("RunFenceTest");
        var tempDir = dir.Path;
        _pathGrantService
            .Setup(s => s.EnsureAccess(
                Sid, It.IsAny<string>(), It.IsAny<SavedRightsState>(),
                It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()))
            .Returns(new GrantOperationResult(GrantAdded: true, TraverseAdded: false, DatabaseModified: true));

        List<string>? pinnedPaths = null;
        _quickAccessPinService
            .Setup(p => p.PinFolders(Sid, It.IsAny<IReadOnlyList<string>>()))
            .Callback<string, IReadOnlyList<string>>((_, paths) => pinnedPaths = [..paths]);

        var helper = CreateHelper();

        // Act
        await helper.GrantFolderAccessAsync([tempDir], Sid, SavedRightsState.DefaultForMode(isDeny: false), _progress.Object);

        // Assert: PinFolders called once with the normalized (full) path of the directory
        _quickAccessPinService.Verify(p => p.PinFolders(Sid, It.IsAny<IReadOnlyList<string>>()), Times.Once);
        Assert.NotNull(pinnedPaths);
        Assert.Single(pinnedPaths);
        Assert.True(Directory.Exists(pinnedPaths[0]));
        Assert.Equal(Path.GetFullPath(pinnedPaths[0]), pinnedPaths[0]); // path is normalized
    }

    [Fact]
    public async Task GrantFolderAccessAsync_DoesNotPin_WhenGrantAlreadyExisted()
    {
        // Arrange: grant returns GrantAdded=false (access already granted)
        using var dir = new TempDirectory("RunFenceTest");
        var tempDir = dir.Path;
        _pathGrantService
            .Setup(s => s.EnsureAccess(
                Sid, It.IsAny<string>(), It.IsAny<SavedRightsState>(),
                It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()))
            .Returns(new GrantOperationResult(GrantAdded: false, TraverseAdded: false, DatabaseModified: false));

        var helper = CreateHelper();

        // Act
        await helper.GrantFolderAccessAsync([tempDir], Sid, SavedRightsState.DefaultForMode(isDeny: false), _progress.Object);

        // Assert: PinFolders never called
        _quickAccessPinService.Verify(
            p => p.PinFolders(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()),
            Times.Never);
    }

    [Fact]
    public async Task GrantFolderAccessAsync_DoesNotPin_WhenPathIsNotDirectory()
    {
        // Arrange: EnsureAccess returns GrantAdded=true but path is a file (not a directory)
        var tempFile = Path.GetTempFileName();
        try
        {
            _pathGrantService
                .Setup(s => s.EnsureAccess(
                    Sid, It.IsAny<string>(), It.IsAny<SavedRightsState>(),
                    It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()))
                .Returns(new GrantOperationResult(GrantAdded: true, TraverseAdded: false, DatabaseModified: true));

            var helper = CreateHelper();

            // Act
            await helper.GrantFolderAccessAsync([tempFile], Sid, SavedRightsState.DefaultForMode(isDeny: false), _progress.Object);

            // Assert: PinFolders not called because path is a file, not a directory
            _quickAccessPinService.Verify(
                p => p.PinFolders(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()),
                Times.Never);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
