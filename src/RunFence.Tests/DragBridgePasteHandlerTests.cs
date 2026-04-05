using System.Security.AccessControl;
using System.Security.Principal;
using Moq;
using RunFence.Acl.Permissions;
using RunFence.Core;
using RunFence.DragBridge;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class DragBridgePasteHandlerTests : IDisposable
{
    private static readonly SecurityIdentifier TargetSid = new("S-1-5-32-545");
    private static readonly string SourceSid = "S-1-5-32-544";

    private readonly Mock<IDragBridgeAccessPrompt> _accessPrompt = new();
    private readonly Mock<IDragBridgeTempFileManager> _tempManager = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<IUiThreadInvoker> _uiThreadInvoker = new();
    private readonly Mock<IAclPermissionService> _aclPermission = new();
    private readonly Mock<IPermissionGrantService> _permissionGrantService = new();
    private readonly SidDisplayNameResolver _displayNameResolver = new(new Mock<ISidResolver>().Object);

    private readonly List<string> _tempFiles = new();
    private readonly List<string> _tempDirs = new();

    public DragBridgePasteHandlerTests()
    {
        // Invoke actions synchronously
        _uiThreadInvoker.Setup(u => u.Invoke(It.IsAny<Action>()))
            .Callback((Action a) => a());
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            if (File.Exists(f))
                File.Delete(f);
        foreach (var d in _tempDirs)
            if (Directory.Exists(d))
                Directory.Delete(d, recursive: true);
    }

    private DragBridgePasteHandler CreateHandler() => new(
        _accessPrompt.Object,
        _tempManager.Object,
        _notifications.Object,
        _log.Object,
        _uiThreadInvoker.Object,
        _aclPermission.Object,
        _permissionGrantService.Object,
        _displayNameResolver);

    private string CreateTempFile()
    {
        var path = Path.GetTempFileName();
        _tempFiles.Add(path);
        return path;
    }

    private string CreateTempDir()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        _tempDirs.Add(dir);
        return dir;
    }

    [Fact]
    public async Task ResolveFileAccess_AllFilesRemoved_ReturnsNullShowsWarning()
    {
        // Arrange: paths that do not exist
        var handler = CreateHandler();
        var paths = new List<string> { @"C:\nonexistent\file.txt" };

        // Act
        var (result, _, granted) = await handler.ResolveFileAccessAsync(TargetSid, paths, SourceSid, null, CancellationToken.None);

        // Assert
        Assert.Null(result);
        Assert.Empty(granted);
        _notifications.Verify(n => n.ShowWarning("Drag Bridge", It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ResolveFileAccess_SourceCantRead_ReturnsNullShowsWarning()
    {
        // Arrange: file exists but source cannot read it
        var file = CreateTempFile();
        _aclPermission.Setup(a => a.NeedsPermissionGrant(file, SourceSid, FileSystemRights.Read))
            .Returns(true);
        var handler = CreateHandler();

        // Act
        var (result, _, granted) = await handler.ResolveFileAccessAsync(TargetSid, [file], SourceSid, null, CancellationToken.None);

        // Assert
        Assert.Null(result);
        Assert.Empty(granted);
        _notifications.Verify(n => n.ShowWarning("Drag Bridge", It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ResolveFileAccess_AllAccessible_ReturnsPathsNoPrompt()
    {
        // Arrange: file exists and both source and target can access it
        var file = CreateTempFile();
        _aclPermission.Setup(a => a.NeedsPermissionGrant(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<FileSystemRights>()))
            .Returns(false);
        var handler = CreateHandler();

        // Act
        var (result, _, granted) = await handler.ResolveFileAccessAsync(TargetSid, [file], SourceSid, null, CancellationToken.None);

        // Assert: file returned as-is, prompt not called
        Assert.NotNull(result);
        Assert.Contains(file, result);
        Assert.Empty(granted);
        _accessPrompt.Verify(p => p.Ask(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task ResolveFileAccess_Inaccessible_GrantAccess_GrantsAndReturns()
    {
        // Arrange: file inaccessible to target; user chooses GrantAccess
        var file = CreateTempFile();
        _aclPermission.Setup(a => a.NeedsPermissionGrant(file, SourceSid, FileSystemRights.Read))
            .Returns(false);
        _aclPermission.Setup(a => a.NeedsPermissionGrant(file, TargetSid.Value, FileSystemRights.Read))
            .Returns(true);
        _accessPrompt.Setup(p => p.Ask(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<long>()))
            .Returns(DragBridgeAccessAction.GrantAccess);
        _permissionGrantService.Setup(g => g.EnsureAccess(file, TargetSid.Value,
                It.IsAny<FileSystemRights>(), null))
            .Returns(new EnsureAccessResult(GrantAdded: true, DatabaseModified: true));
        var handler = CreateHandler();

        // Act
        var (result, _, granted) = await handler.ResolveFileAccessAsync(TargetSid, [file], SourceSid, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Contains(file, result);
        Assert.Contains(file, granted);
        _permissionGrantService.Verify(g => g.EnsureAccess(file, TargetSid.Value,
            It.IsAny<FileSystemRights>(), null), Times.Once);
    }

    [Fact]
    public async Task ResolveFileAccess_Inaccessible_CopyToTemp_ReplacesPathsInResult()
    {
        // Arrange
        var file = CreateTempFile();
        var tempFolder = CreateTempDir();
        var tempCopy = Path.Combine(tempFolder, Path.GetFileName(file));

        _aclPermission.Setup(a => a.NeedsPermissionGrant(file, SourceSid, FileSystemRights.Read))
            .Returns(false);
        _aclPermission.Setup(a => a.NeedsPermissionGrant(file, TargetSid.Value, FileSystemRights.Read))
            .Returns(true);
        _accessPrompt.Setup(p => p.Ask(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<long>()))
            .Returns(DragBridgeAccessAction.CopyToTemp);
        _tempManager.Setup(t => t.CreateTempFolder(TargetSid.Value, null)).Returns(tempFolder);
        _tempManager.Setup(t => t.CopyFilesToTemp(tempFolder, It.IsAny<IReadOnlyList<string>>()))
            .Returns([tempCopy]);
        var handler = CreateHandler();

        // Act
        var (result, _, granted) = await handler.ResolveFileAccessAsync(TargetSid, [file], SourceSid, null, CancellationToken.None);

        // Assert: original path replaced with temp copy
        Assert.NotNull(result);
        Assert.DoesNotContain(file, result);
        Assert.Contains(tempCopy, result);
        Assert.Empty(granted);
        _tempManager.Verify(t => t.CreateTempFolder(TargetSid.Value, null), Times.Once);
        _tempManager.Verify(t => t.CopyFilesToTemp(tempFolder, It.IsAny<IReadOnlyList<string>>()), Times.Once);
    }

    [Fact]
    public async Task ResolveFileAccess_Inaccessible_CopyToTempWholeFolder_CopiesParentDirs()
    {
        // Arrange: a file inside a dir; dir is inaccessible
        var dir = CreateTempDir();
        var file = Path.Combine(dir, "test.txt");
        File.WriteAllText(file, "data");
        _tempFiles.Add(file);

        var tempFolder = CreateTempDir();
        var tempDir = Path.Combine(tempFolder, Path.GetFileName(dir));

        _aclPermission.Setup(a => a.NeedsPermissionGrant(file, SourceSid, FileSystemRights.Read))
            .Returns(false);
        _aclPermission.Setup(a => a.NeedsPermissionGrant(file, TargetSid.Value, FileSystemRights.Read))
            .Returns(true);
        _accessPrompt.Setup(p => p.Ask(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<long>()))
            .Returns(DragBridgeAccessAction.CopyToTempWholeFolder);
        _tempManager.Setup(t => t.CreateTempFolder(TargetSid.Value, null)).Returns(tempFolder);
        _tempManager.Setup(t => t.CopyFilesToTemp(tempFolder, It.Is<IReadOnlyList<string>>(l => l.Contains(dir))))
            .Returns([tempDir]);
        var handler = CreateHandler();

        // Act
        var (result, _, _) = await handler.ResolveFileAccessAsync(TargetSid, [file], SourceSid, null, CancellationToken.None);

        // Assert: CopyFilesToTemp called with the parent directory
        Assert.NotNull(result);
        _tempManager.Verify(t => t.CopyFilesToTemp(tempFolder,
            It.Is<IReadOnlyList<string>>(l => l.Contains(dir))), Times.Once);
    }

    [Fact]
    public async Task ResolveFileAccess_UserCancels_ReturnsNull()
    {
        // Arrange: target cannot access file, user cancels prompt
        var file = CreateTempFile();
        _aclPermission.Setup(a => a.NeedsPermissionGrant(file, SourceSid, FileSystemRights.Read))
            .Returns(false);
        _aclPermission.Setup(a => a.NeedsPermissionGrant(file, TargetSid.Value, FileSystemRights.Read))
            .Returns(true);
        _accessPrompt.Setup(p => p.Ask(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<long>()))
            .Returns((DragBridgeAccessAction?)null);
        var handler = CreateHandler();

        // Act
        var (result, _, granted) = await handler.ResolveFileAccessAsync(TargetSid, [file], SourceSid, null, CancellationToken.None);

        // Assert
        Assert.Null(result);
        Assert.Empty(granted);
    }

    [Fact]
    public async Task ResolveFileAccess_CopyToTempRemembered_SecondCallSkipsPrompt()
    {
        // Arrange: first call — user picks CopyToTemp; second call — same paths, prompt not shown again
        var file = CreateTempFile();
        var tempFolder = CreateTempDir();
        var tempCopy = Path.Combine(tempFolder, Path.GetFileName(file));

        _aclPermission.Setup(a => a.NeedsPermissionGrant(file, SourceSid, FileSystemRights.Read))
            .Returns(false);
        _aclPermission.Setup(a => a.NeedsPermissionGrant(file, TargetSid.Value, FileSystemRights.Read))
            .Returns(true);
        _accessPrompt.Setup(p => p.Ask(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<long>()))
            .Returns(DragBridgeAccessAction.CopyToTemp);
        _tempManager.Setup(t => t.CreateTempFolder(TargetSid.Value, null)).Returns(tempFolder);
        _tempManager.Setup(t => t.CopyFilesToTemp(tempFolder, It.IsAny<IReadOnlyList<string>>()))
            .Returns([tempCopy]);
        var handler = CreateHandler();

        // Act: call twice with same file
        await handler.ResolveFileAccessAsync(TargetSid, [file], SourceSid, null, CancellationToken.None);
        await handler.ResolveFileAccessAsync(TargetSid, [file], SourceSid, null, CancellationToken.None);

        // Assert: prompt shown only once (second call uses remembered choice)
        _accessPrompt.Verify(p => p.Ask(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<long>()), Times.Once);
    }

    [Fact]
    public async Task ResolveFileAccess_GrantAccessNotRemembered_SecondCallPromptsAgain()
    {
        // Arrange: GrantAccess is NOT remembered — second call prompts again
        var file = CreateTempFile();
        _aclPermission.Setup(a => a.NeedsPermissionGrant(file, SourceSid, FileSystemRights.Read))
            .Returns(false);
        _aclPermission.Setup(a => a.NeedsPermissionGrant(file, TargetSid.Value, FileSystemRights.Read))
            .Returns(true);
        _accessPrompt.Setup(p => p.Ask(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<long>()))
            .Returns(DragBridgeAccessAction.GrantAccess);
        _permissionGrantService.Setup(g => g.EnsureAccess(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<FileSystemRights>(), null))
            .Returns(new EnsureAccessResult(GrantAdded: true, DatabaseModified: true));
        var handler = CreateHandler();

        // Act
        await handler.ResolveFileAccessAsync(TargetSid, [file], SourceSid, null, CancellationToken.None);
        await handler.ResolveFileAccessAsync(TargetSid, [file], SourceSid, null, CancellationToken.None);

        // Assert: prompt called twice (GrantAccess not remembered)
        _accessPrompt.Verify(p => p.Ask(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<long>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ResolveFileAccess_GrantAccessPartialFailure_LogsAndContinues()
    {
        // Arrange: two files — EnsureAccess succeeds for first, throws for second
        var file1 = CreateTempFile();
        var file2 = CreateTempFile();

        _aclPermission.Setup(a => a.NeedsPermissionGrant(It.IsAny<string>(), SourceSid, FileSystemRights.Read))
            .Returns(false);
        _aclPermission.Setup(a => a.NeedsPermissionGrant(file1, TargetSid.Value, FileSystemRights.Read))
            .Returns(true);
        _aclPermission.Setup(a => a.NeedsPermissionGrant(file2, TargetSid.Value, FileSystemRights.Read))
            .Returns(true);
        _accessPrompt.Setup(p => p.Ask(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<long>()))
            .Returns(DragBridgeAccessAction.GrantAccess);
        _permissionGrantService.Setup(g => g.EnsureAccess(file1, TargetSid.Value,
                It.IsAny<FileSystemRights>(), null))
            .Returns(new EnsureAccessResult(GrantAdded: true, DatabaseModified: true));
        _permissionGrantService.Setup(g => g.EnsureAccess(file2, TargetSid.Value,
                It.IsAny<FileSystemRights>(), null))
            .Throws(new UnauthorizedAccessException("denied"));
        var handler = CreateHandler();

        // Act
        var (result, _, granted) = await handler.ResolveFileAccessAsync(TargetSid, [file1, file2], SourceSid, null, CancellationToken.None);

        // Assert: both attempted; only file1 in grantedPaths; result returned (not null)
        Assert.NotNull(result);
        Assert.Contains(file1, granted);
        Assert.DoesNotContain(file2, granted);
        _log.Verify(l => l.Warn(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ResolveFileAccess_CopyToTempFailure_ShowsErrorReturnsNull()
    {
        // Arrange: CopyToTemp chosen but CreateTempFolder throws
        var file = CreateTempFile();
        _aclPermission.Setup(a => a.NeedsPermissionGrant(file, SourceSid, FileSystemRights.Read))
            .Returns(false);
        _aclPermission.Setup(a => a.NeedsPermissionGrant(file, TargetSid.Value, FileSystemRights.Read))
            .Returns(true);
        _accessPrompt.Setup(p => p.Ask(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<long>()))
            .Returns(DragBridgeAccessAction.CopyToTemp);
        _tempManager.Setup(t => t.CreateTempFolder(It.IsAny<string>(), It.IsAny<string?>()))
            .Throws(new IOException("disk full"));
        var handler = CreateHandler();

        // Act
        var (result, _, granted) = await handler.ResolveFileAccessAsync(TargetSid, [file], SourceSid, null, CancellationToken.None);

        // Assert
        Assert.Null(result);
        Assert.Empty(granted);
        _notifications.Verify(n => n.ShowError("Drag Bridge", It.IsAny<string>()), Times.Once);
    }
}