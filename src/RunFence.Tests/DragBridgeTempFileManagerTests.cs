using System.Security.AccessControl;
using Moq;
using RunFence.Acl.Permissions;
using RunFence.Acl.Traverse;
using RunFence.Core;
using RunFence.DragBridge;
using Xunit;

namespace RunFence.Tests;

public class DragBridgeTempFileManagerTests : IDisposable
{
    private readonly Mock<ILoggingService> _log;
    private readonly Mock<IAclPermissionService> _aclPermission;
    private readonly string _testBase;
    private readonly DragBridgeTempFileManager _manager;

    public DragBridgeTempFileManagerTests()
    {
        _log = new Mock<ILoggingService>();
        _aclPermission = new Mock<IAclPermissionService>();
        // Return empty group SIDs and report effective rights as covered so no real ACL modification is attempted
        _aclPermission.Setup(a => a.ResolveAccountGroupSids(It.IsAny<string>())).Returns([]);
        _aclPermission.Setup(a => a.HasEffectiveRights(
            It.IsAny<DirectorySecurity>(),
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<FileSystemRights>())).Returns(true);
        var traverseGranter = new AncestorTraverseGranter(_log.Object, _aclPermission.Object);
        _testBase = Path.Combine(Path.GetTempPath(), $"ram_test_{Guid.NewGuid():N}");
        _manager = new DragBridgeTempFileManager(_log.Object, _aclPermission.Object, traverseGranter, _testBase);
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

        var folder = _manager.CreateTempFolder(currentSid);

        Assert.True(Directory.Exists(folder));
        Assert.StartsWith(_testBase, folder);
    }

    [Fact]
    public void CopyFilesToTemp_CopiesFiles()
    {
        using var tempSrc = new TempDirectory();
        var srcFile = Path.Combine(tempSrc.Path, "test.txt");
        File.WriteAllText(srcFile, "hello");

        var currentSid = SidResolutionHelper.GetCurrentUserSid();
        var destFolder = _manager.CreateTempFolder(currentSid);

        var result = _manager.CopyFilesToTemp(destFolder, [srcFile]);

        var expectedDest = Path.Combine(destFolder, "test.txt");
        Assert.True(File.Exists(expectedDest));
        Assert.Equal("hello", File.ReadAllText(expectedDest));
        Assert.Single(result);
        Assert.Equal(expectedDest, result[0]);
    }

    [Fact]
    public void CopyFilesToTemp_HandlesNameCollision_ReturnsActualPath()
    {
        using var tempSrc = new TempDirectory();
        var file2 = Path.Combine(tempSrc.Path, "sub", "file.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(file2)!);
        File.WriteAllText(file2, "second");

        var currentSid = SidResolutionHelper.GetCurrentUserSid();
        var destFolder = _manager.CreateTempFolder(currentSid);

        // Pre-create a conflicting file so the copy must rename
        File.WriteAllText(Path.Combine(destFolder, "file.txt"), "existing");

        var returned = _manager.CopyFilesToTemp(destFolder, [file2]);

        // Should have returned the collision-renamed path, not the original name
        Assert.Single(returned);
        Assert.Equal(Path.Combine(destFolder, "file_1.txt"), returned[0]);
        Assert.True(File.Exists(returned[0]));
        // Original file unchanged
        Assert.Equal("existing", File.ReadAllText(Path.Combine(destFolder, "file.txt")));
    }

    [Fact]
    public void CopyFilesToTemp_NonExistentSource_LogsWarning()
    {
        var currentSid = SidResolutionHelper.GetCurrentUserSid();
        var destFolder = _manager.CreateTempFolder(currentSid);

        var result = _manager.CopyFilesToTemp(destFolder, [@"C:\nonexistent_file_xyz.txt"]);

        _log.Verify(l => l.Warn(It.IsAny<string>()), Times.Once);
        Assert.Empty(result);
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
        var traverseGranter = new AncestorTraverseGranter(_log.Object, _aclPermission.Object);
        var manager = new DragBridgeTempFileManager(_log.Object, _aclPermission.Object, traverseGranter, @"C:\nonexistent_base_xyz");

        var ex = Record.Exception(() => manager.CleanupOldFolders(TimeSpan.FromHours(1)));

        Assert.Null(ex);
    }
}