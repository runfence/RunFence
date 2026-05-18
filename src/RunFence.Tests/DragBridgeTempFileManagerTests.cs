using Moq;
using RunFence.Acl;
using RunFence.Core;
using RunFence.DragBridge;
using Xunit;

namespace RunFence.Tests;

public class DragBridgeTempFileManagerTests : IDisposable
{
    private readonly Mock<ILoggingService> _log;
    private readonly Mock<IPathGrantService> _pathGrantService;
    private readonly Mock<ITempDirectoryAclHelper> _aclHelper;
    private readonly string _testBase;
    private readonly DragBridgeTempFileManager _manager;

    public DragBridgeTempFileManagerTests()
    {
        _log = new Mock<ILoggingService>();
        _pathGrantService = new Mock<IPathGrantService>();
        _aclHelper = new Mock<ITempDirectoryAclHelper>();
        _testBase = Path.Combine(Path.GetTempPath(), $"ram_test_{Guid.NewGuid():N}");
        _manager = new DragBridgeTempFileManager(
            _log.Object,
            _pathGrantService.Object,
            _aclHelper.Object,
            _testBase);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_testBase, true);
        }
        catch
        {
        }
    }

    [Fact]
    public void CreateTempFolder_CreatesDirectory()
    {
        var currentSid = SidResolutionHelper.GetCurrentUserSid();

        var result = _manager.CreateTempFolder(currentSid);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.TempFolderPath);
        Assert.True(Directory.Exists(result.TempFolderPath));
        Assert.StartsWith(_testBase, result.TempFolderPath);
    }

    [Fact]
    public void CopyFilesToTemp_CopiesFiles()
    {
        using var tempSrc = new TempDirectory();
        var srcFile = Path.Combine(tempSrc.Path, "test.txt");
        File.WriteAllText(srcFile, "hello");

        var currentSid = SidResolutionHelper.GetCurrentUserSid();
        var destFolder = _manager.CreateTempFolder(currentSid).TempFolderPath!;

        var result = _manager.CopyFilesToTemp(destFolder, [srcFile]);

        var expectedDest = Path.Combine(destFolder, "test.txt");
        Assert.True(File.Exists(expectedDest));
        Assert.Equal("hello", File.ReadAllText(expectedDest));
        Assert.Single(result.TempPaths);
        Assert.Equal(expectedDest, result.TempPaths[0]);
    }

    [Fact]
    public void CopyFilesToTemp_HandlesNameCollision_ReturnsActualPath()
    {
        using var tempSrc = new TempDirectory();
        var file2 = Path.Combine(tempSrc.Path, "sub", "file.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(file2)!);
        File.WriteAllText(file2, "second");

        var currentSid = SidResolutionHelper.GetCurrentUserSid();
        var destFolder = _manager.CreateTempFolder(currentSid).TempFolderPath!;

        // Pre-create a conflicting file so the copy must rename
        File.WriteAllText(Path.Combine(destFolder, "file.txt"), "existing");

        var returned = _manager.CopyFilesToTemp(destFolder, [file2]);

        // Should have returned the collision-renamed path, not the original name
        Assert.Single(returned.TempPaths);
        Assert.Equal(Path.Combine(destFolder, "file_1.txt"), returned.TempPaths[0]);
        Assert.True(File.Exists(returned.TempPaths[0]));
        // Original file unchanged
        Assert.Equal("existing", File.ReadAllText(Path.Combine(destFolder, "file.txt")));
    }

    [Fact]
    public void CopyFilesToTemp_NonExistentSource_LogsWarning()
    {
        var currentSid = SidResolutionHelper.GetCurrentUserSid();
        var destFolder = _manager.CreateTempFolder(currentSid).TempFolderPath!;

        var result = _manager.CopyFilesToTemp(destFolder, [@"C:\nonexistent_file_xyz.txt"]);

        _log.Verify(l => l.Warn(It.IsAny<string>()), Times.Once);
        Assert.Empty(result.TempPaths);
        Assert.False(Directory.Exists(destFolder));
    }

    [Fact]
    public void CleanupOldFolders_DeletesFoldersOlderThanThreshold()
    {
        Directory.CreateDirectory(_testBase);
        var oldFolder = Path.Combine(_testBase, "old_folder");
        Directory.CreateDirectory(oldFolder);

        // Make the folder appear old by setting creation time
        Directory.SetCreationTimeUtc(oldFolder, DateTime.UtcNow.AddHours(-2));

        _manager.CleanupOldFolders(TimeSpan.FromHours(1));

        Assert.False(Directory.Exists(oldFolder));
    }

    [Fact]
    public void CleanupOldFolders_PreservesRecentFolders()
    {
        Directory.CreateDirectory(_testBase);
        var recentFolder = Path.Combine(_testBase, "recent_folder");
        Directory.CreateDirectory(recentFolder);

        _manager.CleanupOldFolders(TimeSpan.FromHours(1));

        Assert.True(Directory.Exists(recentFolder));
    }

    [Fact]
    public void CleanupOldFolders_NonExistentRoot_DoesNotThrow()
    {
        var manager = new DragBridgeTempFileManager(
            _log.Object,
            _pathGrantService.Object,
            new Mock<ITempDirectoryAclHelper>().Object,
            @"C:\nonexistent_base_xyz");

        var ex = Record.Exception(() => manager.CleanupOldFolders(TimeSpan.FromHours(1)));

        Assert.Null(ex);
    }

    [Fact]
    public void CreateTempFolder_EnsuresTraverseForTargetSid()
    {
        var currentSid = SidResolutionHelper.GetCurrentUserSid();
        _pathGrantService.Setup(s => s.AddTraverse(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new GrantApplyResult(TraverseApplied: true, DatabaseModified: true, DurableSaveCompleted: true));

        var result = _manager.CreateTempFolder(currentSid);

        Assert.True(result.Succeeded);
        _pathGrantService.Verify(s => s.AddTraverse(currentSid, _testBase), Times.Once);
    }

    [Fact]
    public void CreateTempFolder_WithContainerSid_EnsuresTraverseForBothSids()
    {
        var currentSid = SidResolutionHelper.GetCurrentUserSid();
        const string containerSid = "S-1-15-2-42";
        _pathGrantService.Setup(s => s.AddTraverse(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new GrantApplyResult(TraverseApplied: true, DatabaseModified: true, DurableSaveCompleted: true));

        var result = _manager.CreateTempFolder(currentSid, containerSid);

        Assert.True(result.Succeeded);
        _pathGrantService.Verify(s => s.AddTraverse(currentSid, _testBase), Times.Once);
        _pathGrantService.Verify(s => s.AddTraverse(containerSid, _testBase), Times.Once);
    }

    [Fact]
    public void CreateTempFolder_TraverseGrantFailure_ReturnsFailureResult()
    {
        var currentSid = SidResolutionHelper.GetCurrentUserSid();
        _pathGrantService.Setup(s => s.AddTraverse(It.IsAny<string>(), It.IsAny<string>()))
            .Throws(new IOException("save failed"));

        var result = _manager.CreateTempFolder(currentSid);

        Assert.False(result.Succeeded);
        Assert.Null(result.TempFolderPath);
        Assert.Contains("save failed", result.ErrorMessage);
    }
}
