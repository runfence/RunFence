using System.Security.AccessControl;
using System.Security.Principal;
using Moq;
using RunFence.Acl;
using RunFence.Acl.Permissions;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.DragBridge;
using RunFence.Infrastructure;
using RunFence.Persistence;
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
    private readonly Mock<IPathGrantService> _pathGrantService = new();
    private readonly SidDisplayNameResolver _displayNameResolver = new(new Mock<ISidResolver>().Object, new Mock<IProfilePathResolver>().Object);

    private readonly List<string> _tempFiles = new();
    private readonly List<string> _tempDirs = new();

    public DragBridgePasteHandlerTests()
    {
        // Invoke actions synchronously
        _uiThreadInvoker.Setup(u => u.Invoke(It.IsAny<Action>()))
            .Callback((Action a) => a());
        _pathGrantService.Setup(g => g.CaptureGrantRestoreSnapshot(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .Returns(new GrantIntentRestoreSnapshot(null, []));
        _pathGrantService.Setup(g => g.CaptureTraverseRestoreSnapshot(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new GrantIntentRestoreSnapshot(null, []));
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
        _pathGrantService.Object,
        _displayNameResolver,
        new DragBridgeChoiceCache());

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
        var (result, _, granted, _) = await handler.ResolveFileAccessAsync(TargetSid, null, paths, SourceSid, null, null, CancellationToken.None);

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
        var (result, _, granted, _) = await handler.ResolveFileAccessAsync(TargetSid, null, [file], SourceSid, null, null, CancellationToken.None);

        // Assert
        Assert.Null(result);
        Assert.Empty(granted);
        _notifications.Verify(n => n.ShowWarning("Drag Bridge", It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ResolveFileAccess_SourceAppContainerCantRead_ReturnsNullShowsWarning()
    {
        const string sourceContainerSid = "S-1-15-2-42";
        var file = CreateTempFile();
        _aclPermission.Setup(a => a.NeedsPermissionGrant(file, SourceSid, FileSystemRights.Read))
            .Returns(false);
        _aclPermission.Setup(a => a.NeedsPermissionGrant(file, sourceContainerSid, FileSystemRights.Read))
            .Returns(true);
        var handler = CreateHandler();

        var (result, _, granted, _) = await handler.ResolveFileAccessAsync(
            TargetSid,
            null,
            [file],
            SourceSid,
            sourceContainerSid,
            null,
            CancellationToken.None);

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
        var (result, _, granted, _) = await handler.ResolveFileAccessAsync(TargetSid, null, [file], SourceSid, null, null, CancellationToken.None);

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
        _pathGrantService.Setup(g => g.EnsureAccess(TargetSid.Value, file,
                FileSystemRights.Read | FileSystemRights.Synchronize, null, false))
            .Returns(new GrantApplyResult(GrantApplied: true, DatabaseModified: true, DurableSaveCompleted: true));
        var handler = CreateHandler();

        // Act
        var (result, _, granted, _) = await handler.ResolveFileAccessAsync(TargetSid, null, [file], SourceSid, null, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Contains(file, result);
        Assert.Contains(file, granted);
        _pathGrantService.Verify(g => g.EnsureAccess(TargetSid.Value, file,
            FileSystemRights.Read | FileSystemRights.Synchronize, null, false), Times.Once);
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
        _tempManager.Setup(t => t.CreateTempFolder(TargetSid.Value, null))
            .Returns(new DragBridgeTempFolderResult(true, tempFolder, null));
        _tempManager.Setup(t => t.CopyFilesToTemp(tempFolder, It.IsAny<IReadOnlyList<string>>()))
            .Returns(new DragBridgeTempFileResult(true, [], [tempCopy]));
        var handler = CreateHandler();

        // Act
        var (result, _, granted, _) = await handler.ResolveFileAccessAsync(TargetSid, null, [file], SourceSid, null, null, CancellationToken.None);

        // Assert: original path replaced with temp copy
        Assert.NotNull(result);
        Assert.DoesNotContain(file, result);
        Assert.Contains(tempCopy, result);
        Assert.Empty(granted);
        _tempManager.Verify(t => t.CreateTempFolder(TargetSid.Value, null), Times.Once);
        _tempManager.Verify(t => t.CopyFilesToTemp(tempFolder, It.IsAny<IReadOnlyList<string>>()), Times.Once);
    }

    [Fact]
    public async Task ResolveFileAccess_AppContainerCopyToTemp_PassesContainerSidToTempFolder()
    {
        const string containerSidValue = "S-1-15-2-42";
        var containerSid = new SecurityIdentifier(containerSidValue);
        var file = CreateTempFile();
        var tempFolder = CreateTempDir();
        var tempCopy = Path.Combine(tempFolder, Path.GetFileName(file));

        _aclPermission.Setup(a => a.NeedsPermissionGrant(file, SourceSid, FileSystemRights.Read))
            .Returns(false);
        _aclPermission.Setup(a => a.NeedsPermissionGrant(file, TargetSid.Value, FileSystemRights.Read))
            .Returns(false);
        _aclPermission.Setup(a => a.NeedsPermissionGrant(file, containerSidValue, FileSystemRights.Read))
            .Returns(true);
        _accessPrompt.Setup(p => p.Ask(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<long>()))
            .Returns(DragBridgeAccessAction.CopyToTemp);
        _tempManager.Setup(t => t.CreateTempFolder(TargetSid.Value, containerSidValue))
            .Returns(new DragBridgeTempFolderResult(true, tempFolder, null));
        _tempManager.Setup(t => t.CopyFilesToTemp(tempFolder, It.IsAny<IReadOnlyList<string>>()))
            .Returns(new DragBridgeTempFileResult(true, [], [tempCopy]));
        var handler = CreateHandler();

        var (result, _, granted, _) = await handler.ResolveFileAccessAsync(
            TargetSid,
            containerSid,
            [file],
            SourceSid,
            null,
            null,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.DoesNotContain(file, result);
        Assert.Contains(tempCopy, result);
        Assert.Empty(granted);
        _tempManager.Verify(t => t.CreateTempFolder(TargetSid.Value, containerSidValue), Times.Once);
    }

    [Fact]
    public void NeedsAccessResolution_AppContainerNeedsGrant_ReturnsTrue()
    {
        const string containerSidValue = "S-1-15-2-42";
        var containerSid = new SecurityIdentifier(containerSidValue);
        var file = CreateTempFile();
        _aclPermission.Setup(a => a.NeedsPermissionGrant(file, TargetSid.Value, FileSystemRights.Read))
            .Returns(false);
        _aclPermission.Setup(a => a.NeedsPermissionGrant(file, containerSidValue, FileSystemRights.Read))
            .Returns(true);
        var handler = CreateHandler();

        Assert.True(handler.NeedsAccessResolution(TargetSid, containerSid, [file]));
    }

    [Fact]
    public async Task ResolveFileAccess_Inaccessible_GrantFolderAccess_GrantsParentDirAndReturnsOriginalPaths()
    {
        // Arrange: a file inside a dir; user chooses GrantFolderAccess
        var dir = CreateTempDir();
        var file = Path.Combine(dir, "test.txt");
        await File.WriteAllTextAsync(file, "data");
        _tempFiles.Add(file);

        _aclPermission.Setup(a => a.NeedsPermissionGrant(file, SourceSid, FileSystemRights.Read))
            .Returns(false);
        _aclPermission.Setup(a => a.NeedsPermissionGrant(file, TargetSid.Value, FileSystemRights.Read))
            .Returns(true);
        _accessPrompt.Setup(p => p.Ask(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<long>()))
            .Returns(DragBridgeAccessAction.GrantFolderAccess);
        _pathGrantService.Setup(g => g.EnsureAccess(TargetSid.Value, dir,
                FileSystemRights.ReadAndExecute, null, false))
            .Returns(new GrantApplyResult(GrantApplied: true, DatabaseModified: true, DurableSaveCompleted: true));
        var handler = CreateHandler();

        // Act
        var (result, _, granted, _) = await handler.ResolveFileAccessAsync(TargetSid, null, [file], SourceSid, null, null, CancellationToken.None);

        // Assert: EnsureAccess called on parent dir; original path returned (no temp copy)
        Assert.NotNull(result);
        Assert.Contains(file, result);
        Assert.Contains(dir, granted);
        _pathGrantService.Verify(g => g.EnsureAccess(TargetSid.Value, dir,
            FileSystemRights.ReadAndExecute, null, false), Times.Once);
        _tempManager.Verify(t => t.CreateTempFolder(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task ResolveFileAccess_Inaccessible_GrantFolderAccess_DirectoryPath_GrantsDirectoryItself()
    {
        // Arrange: an inaccessible directory itself (not a file inside it)
        var dir = CreateTempDir();

        _aclPermission.Setup(a => a.NeedsPermissionGrant(dir, SourceSid, FileSystemRights.Read))
            .Returns(false);
        _aclPermission.Setup(a => a.NeedsPermissionGrant(dir, TargetSid.Value, FileSystemRights.Read))
            .Returns(true);
        _accessPrompt.Setup(p => p.Ask(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<long>()))
            .Returns(DragBridgeAccessAction.GrantFolderAccess);
        _pathGrantService.Setup(g => g.EnsureAccess(TargetSid.Value, dir,
                FileSystemRights.ReadAndExecute, null, false))
            .Returns(new GrantApplyResult(GrantApplied: true, DatabaseModified: true, DurableSaveCompleted: true));
        var handler = CreateHandler();

        // Act
        var (result, _, granted, _) = await handler.ResolveFileAccessAsync(TargetSid, null, [dir], SourceSid, null, null, CancellationToken.None);

        // Assert: EnsureAccess called on the directory itself
        Assert.NotNull(result);
        Assert.Contains(dir, result);
        Assert.Contains(dir, granted);
        _pathGrantService.Verify(g => g.EnsureAccess(TargetSid.Value, dir,
            FileSystemRights.ReadAndExecute, null, false), Times.Once);
    }

    [Fact]
    public async Task ResolveFileAccess_GrantFolderAccessNotRemembered_SecondCallPromptsAgain()
    {
        // Arrange: GrantFolderAccess is NOT remembered — second call prompts again
        var dir = CreateTempDir();
        var file = Path.Combine(dir, "test.txt");
        await File.WriteAllTextAsync(file, "data");
        _tempFiles.Add(file);

        _aclPermission.Setup(a => a.NeedsPermissionGrant(file, SourceSid, FileSystemRights.Read))
            .Returns(false);
        _aclPermission.Setup(a => a.NeedsPermissionGrant(file, TargetSid.Value, FileSystemRights.Read))
            .Returns(true);
        _accessPrompt.Setup(p => p.Ask(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<long>()))
            .Returns(DragBridgeAccessAction.GrantFolderAccess);
        _pathGrantService.Setup(g => g.EnsureAccess(It.IsAny<string>(), It.IsAny<string>(),
                FileSystemRights.ReadAndExecute, null, false))
            .Returns(new GrantApplyResult(GrantApplied: true, DatabaseModified: true, DurableSaveCompleted: true));
        var handler = CreateHandler();

        // Act
        await handler.ResolveFileAccessAsync(TargetSid, null, [file], SourceSid, null, null, CancellationToken.None);
        await handler.ResolveFileAccessAsync(TargetSid, null, [file], SourceSid, null, null, CancellationToken.None);

        // Assert: prompt called twice (GrantFolderAccess not remembered)
        _accessPrompt.Verify(p => p.Ask(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<long>()), Times.Exactly(2));
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
        var (result, _, granted, _) = await handler.ResolveFileAccessAsync(TargetSid, null, [file], SourceSid, null, null, CancellationToken.None);

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
        _tempManager.Setup(t => t.CreateTempFolder(TargetSid.Value, null))
            .Returns(new DragBridgeTempFolderResult(true, tempFolder, null));
        _tempManager.Setup(t => t.CopyFilesToTemp(tempFolder, It.IsAny<IReadOnlyList<string>>()))
            .Returns(new DragBridgeTempFileResult(true, [], [tempCopy]));
        var handler = CreateHandler();

        // Act: call twice with same file
        await handler.ResolveFileAccessAsync(TargetSid, null, [file], SourceSid, null, null, CancellationToken.None);
        await handler.ResolveFileAccessAsync(TargetSid, null, [file], SourceSid, null, null, CancellationToken.None);

        // Assert: prompt shown only once (second call uses remembered choice)
        _accessPrompt.Verify(p => p.Ask(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<long>()), Times.Once);
    }

    [Fact]
    public async Task ResolveFileAccess_CopyToTempRemembered_DifferentContainerPromptsAgain()
    {
        const string firstContainerSidValue = "S-1-15-2-42";
        const string secondContainerSidValue = "S-1-15-2-43";
        var file = CreateTempFile();
        var tempFolder = CreateTempDir();
        var tempCopy = Path.Combine(tempFolder, Path.GetFileName(file));

        _aclPermission.Setup(a => a.NeedsPermissionGrant(file, SourceSid, FileSystemRights.Read))
            .Returns(false);
        _aclPermission.Setup(a => a.NeedsPermissionGrant(file, TargetSid.Value, FileSystemRights.Read))
            .Returns(false);
        _aclPermission.Setup(a => a.NeedsPermissionGrant(file, firstContainerSidValue, FileSystemRights.Read))
            .Returns(true);
        _aclPermission.Setup(a => a.NeedsPermissionGrant(file, secondContainerSidValue, FileSystemRights.Read))
            .Returns(true);
        _accessPrompt.Setup(p => p.Ask(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<long>()))
            .Returns(DragBridgeAccessAction.CopyToTemp);
        _tempManager.Setup(t => t.CreateTempFolder(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(new DragBridgeTempFolderResult(true, tempFolder, null));
        _tempManager.Setup(t => t.CopyFilesToTemp(tempFolder, It.IsAny<IReadOnlyList<string>>()))
            .Returns(new DragBridgeTempFileResult(true, [], [tempCopy]));
        var handler = CreateHandler();

        await handler.ResolveFileAccessAsync(
            TargetSid,
            new SecurityIdentifier(firstContainerSidValue),
            [file],
            SourceSid,
            null,
            null,
            CancellationToken.None);
        await handler.ResolveFileAccessAsync(
            TargetSid,
            new SecurityIdentifier(secondContainerSidValue),
            [file],
            SourceSid,
            null,
            null,
            CancellationToken.None);

        _accessPrompt.Verify(p => p.Ask(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<long>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ResolveFileAccess_AppContainerGrantFolderAccess_DoesNotGrantCurrentUserWhenOnlyContainerNeedsAccess()
    {
        const string containerSidValue = "S-1-15-2-42";
        var containerSid = new SecurityIdentifier(containerSidValue);
        var dir = CreateTempDir();
        var file = Path.Combine(dir, "test.txt");
        await File.WriteAllTextAsync(file, "data");
        _tempFiles.Add(file);

        _aclPermission.Setup(a => a.NeedsPermissionGrant(file, SourceSid, FileSystemRights.Read))
            .Returns(false);
        _aclPermission.Setup(a => a.NeedsPermissionGrant(file, TargetSid.Value, FileSystemRights.Read))
            .Returns(false);
        _aclPermission.Setup(a => a.NeedsPermissionGrant(file, containerSidValue, FileSystemRights.Read))
            .Returns(true);
        _accessPrompt.Setup(p => p.Ask(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<long>()))
            .Returns(DragBridgeAccessAction.GrantFolderAccess);
        _pathGrantService.Setup(g => g.EnsureAccess(containerSidValue, dir,
                FileSystemRights.ReadAndExecute, null, false))
            .Returns(new GrantApplyResult(GrantApplied: true, DatabaseModified: true, DurableSaveCompleted: true));
        var handler = CreateHandler();

        var result = await handler.ResolveFileAccessAsync(
            TargetSid,
            containerSid,
            [file],
            SourceSid,
            null,
            null,
            CancellationToken.None);

        Assert.NotNull(result.Paths);
        _pathGrantService.Verify(g => g.EnsureAccess(containerSidValue, dir,
            FileSystemRights.ReadAndExecute, null, false), Times.Once);
        _pathGrantService.Verify(g => g.EnsureAccess(TargetSid.Value, dir,
            FileSystemRights.ReadAndExecute, null, false), Times.Never);
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
        _pathGrantService.Setup(g => g.EnsureAccess(It.IsAny<string>(), It.IsAny<string>(),
                FileSystemRights.Read | FileSystemRights.Synchronize, null, false))
            .Returns(new GrantApplyResult(GrantApplied: true, DatabaseModified: true, DurableSaveCompleted: true));
        var handler = CreateHandler();

        // Act
        await handler.ResolveFileAccessAsync(TargetSid, null, [file], SourceSid, null, null, CancellationToken.None);
        await handler.ResolveFileAccessAsync(TargetSid, null, [file], SourceSid, null, null, CancellationToken.None);

        // Assert: prompt called twice (GrantAccess not remembered)
        _accessPrompt.Verify(p => p.Ask(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<long>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ResolveFileAccess_GrantAccessFailure_RollsBackAndReturnsNull()
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
        _pathGrantService.Setup(g => g.EnsureAccess(TargetSid.Value, file1,
                FileSystemRights.Read | FileSystemRights.Synchronize, null, false))
            .Returns(new GrantApplyResult(GrantApplied: true, DatabaseModified: true, DurableSaveCompleted: true));
        _pathGrantService.Setup(g => g.EnsureAccess(TargetSid.Value, file2,
                FileSystemRights.Read | FileSystemRights.Synchronize, null, false))
            .Throws(new UnauthorizedAccessException("denied"));
        var handler = CreateHandler();

        // Act
        var (result, _, granted, _) = await handler.ResolveFileAccessAsync(TargetSid, null, [file1, file2], SourceSid, null, null, CancellationToken.None);

        // Assert: failed operation aborts and removes previously granted file1.
        Assert.Null(result);
        Assert.Empty(granted);
        _log.Verify(l => l.Warn(It.IsAny<string>()), Times.Once);
        _pathGrantService.Verify(g => g.RestoreGrant(TargetSid.Value, file1, false, It.Is<GrantIntentRestoreSnapshot>(snapshot => snapshot.RuntimeEntry == null && snapshot.Locations.Count == 0)), Times.Once);
    }

    [Fact]
    public async Task ResolveFileAccess_GrantAccessFailure_DoesNotRollbackAclRepairOnly()
    {
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
        _pathGrantService.Setup(g => g.EnsureAccess(TargetSid.Value, file1,
                FileSystemRights.Read | FileSystemRights.Synchronize, null, false))
            .Returns(new GrantApplyResult(GrantApplied: true, DatabaseModified: false, DurableSaveCompleted: false));
        _pathGrantService.Setup(g => g.EnsureAccess(TargetSid.Value, file2,
                FileSystemRights.Read | FileSystemRights.Synchronize, null, false))
            .Throws(new UnauthorizedAccessException("denied"));
        var handler = CreateHandler();

        var database = new AppDatabase();
        database.GetOrCreateAccount(TargetSid.Value).Grants.Add(new GrantedPathEntry
        {
            Path = Path.GetFullPath(file1),
            IsDeny = false,
            SavedRights = SavedRightsState.DefaultForMode(isDeny: false)
        });

        var (result, _, granted, _) = await handler.ResolveFileAccessAsync(TargetSid, null, [file1, file2], SourceSid, null, database, CancellationToken.None);

        Assert.Null(result);
        Assert.Empty(granted);
        _pathGrantService.Verify(g => g.RestoreGrant(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<GrantIntentRestoreSnapshot>()), Times.Never);
        _pathGrantService.Verify(g => g.RestoreTraverse(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<GrantIntentRestoreSnapshot>()), Times.Never);
    }

    [Fact]
    public async Task ResolveFileAccess_GrantAccessFailure_RestoresLegacyGrantSnapshotExactly()
    {
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
        _pathGrantService.Setup(g => g.EnsureAccess(TargetSid.Value, file1,
                FileSystemRights.Read | FileSystemRights.Synchronize, null, false))
            .Returns(new GrantApplyResult(GrantApplied: true, DatabaseModified: true, DurableSaveCompleted: true));
        _pathGrantService.Setup(g => g.EnsureAccess(TargetSid.Value, file2,
                FileSystemRights.Read | FileSystemRights.Synchronize, null, false))
            .Throws(new UnauthorizedAccessException("denied"));
        var database = new AppDatabase();
        database.GetOrCreateAccount(TargetSid.Value).Grants.Add(new GrantedPathEntry
        {
            Path = Path.GetFullPath(file1),
            IsDeny = false,
            SavedRights = null
        });
        _pathGrantService.Setup(g => g.CaptureGrantRestoreSnapshot(TargetSid.Value, file1, false))
            .Returns(new GrantIntentRestoreSnapshot(
                new GrantedPathEntry
                {
                    Path = Path.GetFullPath(file1),
                    IsDeny = false,
                    SavedRights = null
                },
                [new GrantIntentRestoreLocation(null, new GrantedPathEntry
                {
                    Path = Path.GetFullPath(file1),
                    IsDeny = false,
                    SavedRights = null
                })]));
        var handler = CreateHandler();

        var (result, _, granted, _) = await handler.ResolveFileAccessAsync(
            TargetSid,
            null,
            [file1, file2],
            SourceSid,
            null,
            database,
            CancellationToken.None);

        Assert.Null(result);
        Assert.Empty(granted);
        _pathGrantService.Verify(g => g.RestoreGrant(
            TargetSid.Value,
            file1,
            false,
            It.Is<GrantIntentRestoreSnapshot>(snapshot =>
                snapshot.RuntimeEntry != null &&
                string.Equals(snapshot.RuntimeEntry.Path, Path.GetFullPath(file1), StringComparison.OrdinalIgnoreCase) &&
                snapshot.RuntimeEntry.SavedRights == null &&
                snapshot.Locations.Count == 1 &&
                snapshot.Locations[0].Entry.SavedRights == null)), Times.Once);
    }

    [Fact]
    public async Task ResolveFileAccess_GrantAccessFailure_RestoresLegacyTraverseSnapshotExactly()
    {
        var file1 = CreateTempFile();
        var file2 = CreateTempFile();
        var traversePath = Path.GetDirectoryName(Path.GetFullPath(file1))!;

        _aclPermission.Setup(a => a.NeedsPermissionGrant(It.IsAny<string>(), SourceSid, FileSystemRights.Read))
            .Returns(false);
        _aclPermission.Setup(a => a.NeedsPermissionGrant(file1, TargetSid.Value, FileSystemRights.Read))
            .Returns(true);
        _aclPermission.Setup(a => a.NeedsPermissionGrant(file2, TargetSid.Value, FileSystemRights.Read))
            .Returns(true);
        _accessPrompt.Setup(p => p.Ask(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<long>()))
            .Returns(DragBridgeAccessAction.GrantAccess);
        _pathGrantService.Setup(g => g.EnsureAccess(TargetSid.Value, file1,
                FileSystemRights.Read | FileSystemRights.Synchronize, null, false))
            .Returns(new GrantApplyResult(GrantApplied: false, DatabaseModified: true, DurableSaveCompleted: true));
        _pathGrantService.Setup(g => g.EnsureAccess(TargetSid.Value, file2,
                FileSystemRights.Read | FileSystemRights.Synchronize, null, false))
            .Throws(new UnauthorizedAccessException("denied"));
        var database = new AppDatabase();
        database.GetOrCreateAccount(TargetSid.Value).Grants.Add(new GrantedPathEntry
        {
            Path = Path.GetFullPath(file1),
            IsDeny = false,
            SavedRights = SavedRightsState.DefaultForMode(isDeny: false)
        });
        database.GetOrCreateAccount(TargetSid.Value).Grants.Add(new GrantedPathEntry
        {
            Path = traversePath,
            IsTraverseOnly = true,
            AllAppliedPaths = null
        });
        _pathGrantService.Setup(g => g.CaptureGrantRestoreSnapshot(TargetSid.Value, file1, false))
            .Returns(new GrantIntentRestoreSnapshot(
                new GrantedPathEntry
                {
                    Path = Path.GetFullPath(file1),
                    IsDeny = false,
                    SavedRights = SavedRightsState.DefaultForMode(isDeny: false)
                },
                [new GrantIntentRestoreLocation(null, new GrantedPathEntry
                {
                    Path = Path.GetFullPath(file1),
                    IsDeny = false,
                    SavedRights = SavedRightsState.DefaultForMode(isDeny: false)
                })]));
        _pathGrantService.Setup(g => g.CaptureTraverseRestoreSnapshot(TargetSid.Value, traversePath))
            .Returns(new GrantIntentRestoreSnapshot(
                new GrantedPathEntry
                {
                    Path = traversePath,
                    IsTraverseOnly = true,
                    AllAppliedPaths = null
                },
                [new GrantIntentRestoreLocation(null, new GrantedPathEntry
                {
                    Path = traversePath,
                    IsTraverseOnly = true,
                    AllAppliedPaths = null
                })]));
        var handler = CreateHandler();

        var (result, _, granted, _) = await handler.ResolveFileAccessAsync(
            TargetSid,
            null,
            [file1, file2],
            SourceSid,
            null,
            database,
            CancellationToken.None);

        Assert.Null(result);
        Assert.Empty(granted);
        _pathGrantService.Verify(g => g.RestoreTraverse(
            TargetSid.Value,
            traversePath,
            It.Is<GrantIntentRestoreSnapshot>(snapshot =>
                snapshot.RuntimeEntry != null &&
                string.Equals(snapshot.RuntimeEntry.Path, traversePath, StringComparison.OrdinalIgnoreCase) &&
                snapshot.RuntimeEntry.AllAppliedPaths == null &&
                snapshot.Locations.Count == 1 &&
                snapshot.Locations[0].Entry.AllAppliedPaths == null)), Times.Once);
    }

    [Fact]
    public async Task ResolveFileAccess_GrantFolderAccessFailure_RestoresPersistedExistingExecutableGrantSnapshot()
    {
        var dir1 = CreateTempDir();
        var dir2 = CreateTempDir();
        var file1 = Path.Combine(dir1, "one.txt");
        var file2 = Path.Combine(dir2, "two.txt");
        await File.WriteAllTextAsync(file1, "1");
        await File.WriteAllTextAsync(file2, "2");
        _tempFiles.Add(file1);
        _tempFiles.Add(file2);

        _aclPermission.Setup(a => a.NeedsPermissionGrant(file1, SourceSid, FileSystemRights.Read))
            .Returns(false);
        _aclPermission.Setup(a => a.NeedsPermissionGrant(file2, SourceSid, FileSystemRights.Read))
            .Returns(false);
        _aclPermission.Setup(a => a.NeedsPermissionGrant(file1, TargetSid.Value, FileSystemRights.Read))
            .Returns(true);
        _aclPermission.Setup(a => a.NeedsPermissionGrant(file2, TargetSid.Value, FileSystemRights.Read))
            .Returns(true);
        _accessPrompt.Setup(p => p.Ask(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<long>()))
            .Returns(DragBridgeAccessAction.GrantFolderAccess);
        _pathGrantService.Setup(g => g.EnsureAccess(TargetSid.Value, dir1,
                FileSystemRights.ReadAndExecute, null, false))
            .Returns(new GrantApplyResult(GrantApplied: false, DatabaseModified: true, DurableSaveCompleted: true));
        _pathGrantService.Setup(g => g.EnsureAccess(TargetSid.Value, dir2,
                FileSystemRights.ReadAndExecute, null, false))
            .Throws(new UnauthorizedAccessException("denied"));
        _pathGrantService.Setup(g => g.CaptureGrantRestoreSnapshot(TargetSid.Value, dir1, false))
            .Returns(new GrantIntentRestoreSnapshot(
                new GrantedPathEntry
                {
                    Path = Path.GetFullPath(dir1),
                    IsDeny = false,
                    SavedRights = new SavedRightsState(
                        Execute: true,
                        Write: false,
                        Read: true,
                        Special: false,
                        Own: false)
                },
                [new GrantIntentRestoreLocation(null, new GrantedPathEntry
                {
                    Path = Path.GetFullPath(dir1),
                    IsDeny = false,
                    SavedRights = new SavedRightsState(
                        Execute: true,
                        Write: false,
                        Read: true,
                        Special: false,
                        Own: false)
                })]));
        var handler = CreateHandler();

        var (result, _, granted, _) = await handler.ResolveFileAccessAsync(
            TargetSid,
            null,
            [file1, file2],
            SourceSid,
            null,
            new AppDatabase(),
            CancellationToken.None);

        Assert.Null(result);
        Assert.Empty(granted);
        _pathGrantService.Verify(g => g.RestoreGrant(
            TargetSid.Value,
            dir1,
            false,
            It.Is<GrantIntentRestoreSnapshot>(snapshot =>
                snapshot.RuntimeEntry != null &&
                snapshot.RuntimeEntry.SavedRights != null &&
                string.Equals(snapshot.RuntimeEntry!.Path, Path.GetFullPath(dir1), StringComparison.OrdinalIgnoreCase) &&
                snapshot.RuntimeEntry.SavedRights!.Execute &&
                snapshot.Locations.Count == 1 &&
                snapshot.Locations[0].Entry.SavedRights != null &&
                snapshot.Locations[0].Entry.SavedRights!.Execute)), Times.Once);
    }

    [Fact]
    public async Task ResolveFileAccess_MixedDurablePersistence_ReportsDurabilityFailure()
    {
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
        _pathGrantService.Setup(g => g.EnsureAccess(TargetSid.Value, file1,
                FileSystemRights.Read | FileSystemRights.Synchronize, null, false))
            .Returns(new GrantApplyResult(GrantApplied: true, DatabaseModified: true, DurableSaveCompleted: true));
        _pathGrantService.Setup(g => g.EnsureAccess(TargetSid.Value, file2,
                FileSystemRights.Read | FileSystemRights.Synchronize, null, false))
            .Returns(new GrantApplyResult(GrantApplied: true, DatabaseModified: true, DurableSaveCompleted: false));
        var handler = CreateHandler();

        var resolveResult = await handler.ResolveFileAccessAsync(
            TargetSid,
            null,
            [file1, file2],
            SourceSid,
            null,
            null,
            CancellationToken.None);

        Assert.True(resolveResult.DatabaseModified);
        Assert.False(resolveResult.DurableSaveCompleted);
    }

    [Fact]
    public async Task ResolveFileAccess_MixedDurablePersistence_ReturnsDurableWarnings()
    {
        var file1 = CreateTempFile();
        var file2 = CreateTempFile();
        var warning = new GrantApplyWarning(
            GrantApplyFailureStep.PostGrantMutationSave,
            @"C:\app\test.txt",
            null,
            new InvalidOperationException("launch warning"));

        _aclPermission.Setup(a => a.NeedsPermissionGrant(It.IsAny<string>(), SourceSid, FileSystemRights.Read))
            .Returns(false);
        _aclPermission.Setup(a => a.NeedsPermissionGrant(file1, TargetSid.Value, FileSystemRights.Read))
            .Returns(true);
        _aclPermission.Setup(a => a.NeedsPermissionGrant(file2, TargetSid.Value, FileSystemRights.Read))
            .Returns(true);
        _accessPrompt.Setup(p => p.Ask(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<long>()))
            .Returns(DragBridgeAccessAction.GrantAccess);
        _pathGrantService.Setup(g => g.EnsureAccess(TargetSid.Value, file1,
                FileSystemRights.Read | FileSystemRights.Synchronize, null, false))
            .Returns(new GrantApplyResult(GrantApplied: true, DatabaseModified: true, DurableSaveCompleted: true));
        _pathGrantService.Setup(g => g.EnsureAccess(TargetSid.Value, file2,
                FileSystemRights.Read | FileSystemRights.Synchronize, null, false))
            .Returns(new GrantApplyResult(
                GrantApplied: true,
                DatabaseModified: true,
                DurableSaveCompleted: false,
                Warnings: [warning]));
        var handler = CreateHandler();

        var resolveResult = await handler.ResolveFileAccessAsync(
            TargetSid,
            null,
            [file1, file2],
            SourceSid,
            null,
            null,
            CancellationToken.None);

        Assert.True(resolveResult.DatabaseModified);
        Assert.False(resolveResult.DurableSaveCompleted);
        Assert.Single(resolveResult.Warnings);
        Assert.Contains(resolveResult.Warnings, text => text.Contains("launch warning", StringComparison.Ordinal));
    }

    // ── NeedsAccessResolution ─────────────────────────────────────────────

    [Fact]
    public void NeedsAccessResolution_AllAccessible_ReturnsFalse()
    {
        var file = CreateTempFile();
        _aclPermission.Setup(a => a.NeedsPermissionGrant(file, TargetSid.Value, FileSystemRights.Read))
            .Returns(false);
        var handler = CreateHandler();

        Assert.False(handler.NeedsAccessResolution(TargetSid, null, [file]));
    }

    [Fact]
    public void NeedsAccessResolution_FileNotExist_ReturnsTrue()
    {
        var handler = CreateHandler();

        Assert.True(handler.NeedsAccessResolution(TargetSid, null, [@"C:\nonexistent\file.txt"]));
    }

    [Fact]
    public void NeedsAccessResolution_SomeInaccessible_ReturnsTrue()
    {
        var file = CreateTempFile();
        _aclPermission.Setup(a => a.NeedsPermissionGrant(file, TargetSid.Value, FileSystemRights.Read))
            .Returns(true);
        var handler = CreateHandler();

        Assert.True(handler.NeedsAccessResolution(TargetSid, null, [file]));
    }

    [Fact]
    public async Task ResolveFileAccess_PreCancelledToken_ReturnsNullNoGrantsApplied()
    {
        // Arrange: file exists and target needs access — but token is pre-cancelled.
        // When the token is cancelled the prompt ask-result is discarded and null is returned,
        // ensuring no grant operations were attempted.
        var file = CreateTempFile();
        _aclPermission.Setup(a => a.NeedsPermissionGrant(file, SourceSid, FileSystemRights.Read))
            .Returns(false);
        _aclPermission.Setup(a => a.NeedsPermissionGrant(file, TargetSid.Value, FileSystemRights.Read))
            .Returns(true);
        _accessPrompt.Setup(p => p.Ask(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<long>()))
            .Returns((DragBridgeAccessAction?)null); // prompt returns cancel when called
        var handler = CreateHandler();

        // Act
        var (result, _, granted, _) = await handler.ResolveFileAccessAsync(
            TargetSid, null, [file], SourceSid, null, null, new CancellationToken(true));

        // Assert: null result returned (cancelled prompt) and no grants applied
        Assert.Null(result);
        Assert.Empty(granted);
        _accessPrompt.Verify(p => p.Ask(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<long>()), Times.Never);
        _tempManager.Verify(t => t.CreateTempFolder(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
        _tempManager.Verify(t => t.CopyFilesToTemp(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>()), Times.Never);
        _pathGrantService.Verify(g => g.EnsureAccess(
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<FileSystemRights>(), It.IsAny<Func<string, string, bool>?>(), It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public async Task ResolveFileAccess_CopyToTempPartialFailure_RemovesReturnedTempPaths()
    {
        var file = CreateTempFile();
        var tempFolder = CreateTempDir();
        var tempCopy = Path.Combine(tempFolder, Path.GetFileName(file));
        await File.WriteAllTextAsync(tempCopy, "copied");
        _tempFiles.Add(tempCopy);

        _aclPermission.Setup(a => a.NeedsPermissionGrant(file, SourceSid, FileSystemRights.Read))
            .Returns(false);
        _aclPermission.Setup(a => a.NeedsPermissionGrant(file, TargetSid.Value, FileSystemRights.Read))
            .Returns(true);
        _accessPrompt.Setup(p => p.Ask(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<long>()))
            .Returns(DragBridgeAccessAction.CopyToTemp);
        _tempManager.Setup(t => t.CreateTempFolder(TargetSid.Value, null))
            .Returns(new DragBridgeTempFolderResult(true, tempFolder, null));
        _tempManager.Setup(t => t.CopyFilesToTemp(tempFolder, It.IsAny<IReadOnlyList<string>>()))
            .Returns(new DragBridgeTempFileResult(false,
                [
                    new DragBridgeTempFileEntryResult(
                        file,
                        tempCopy,
                        DragBridgeTempFileCopyStatus.Failed,
                        DragBridgeTempFileGrantStatus.NotAttempted,
                        DragBridgeTempFileRollbackStatus.NotRequired,
                        "copy failed")
                ],
                [tempCopy]));

        var handler = CreateHandler();

        var (result, _, granted, _) = await handler.ResolveFileAccessAsync(
            TargetSid, null, [file], SourceSid, null, null, CancellationToken.None);

        Assert.Null(result);
        Assert.Empty(granted);
        Assert.False(File.Exists(tempCopy));
        _notifications.Verify(n => n.ShowError("Drag Bridge", It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ResolveFileAccess_CopyToTempFailure_ShowsErrorReturnsNull()
    {
        // Arrange: CopyToTemp chosen but CreateTempFolder reports failure
        var file = CreateTempFile();
        _aclPermission.Setup(a => a.NeedsPermissionGrant(file, SourceSid, FileSystemRights.Read))
            .Returns(false);
        _aclPermission.Setup(a => a.NeedsPermissionGrant(file, TargetSid.Value, FileSystemRights.Read))
            .Returns(true);
        _accessPrompt.Setup(p => p.Ask(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<long>()))
            .Returns(DragBridgeAccessAction.CopyToTemp);
        _tempManager.Setup(t => t.CreateTempFolder(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(new DragBridgeTempFolderResult(false, null, "disk full"));
        var handler = CreateHandler();

        // Act
        var (result, _, granted, _) = await handler.ResolveFileAccessAsync(TargetSid, null, [file], SourceSid, null, null, CancellationToken.None);

        // Assert
        Assert.Null(result);
        Assert.Empty(granted);
        _notifications.Verify(n => n.ShowError("Drag Bridge", It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ResolveFileAccess_GrantAccessFailure_RollsBackTraverseOnlyChange()
    {
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
        _pathGrantService.Setup(g => g.EnsureAccess(TargetSid.Value, file1,
                FileSystemRights.Read | FileSystemRights.Synchronize, null, false))
            .Returns(new GrantApplyResult(GrantApplied: false, TraverseApplied: true, DatabaseModified: true, DurableSaveCompleted: true));
        _pathGrantService.Setup(g => g.EnsureAccess(TargetSid.Value, file2,
                FileSystemRights.Read | FileSystemRights.Synchronize, null, false))
            .Throws(new UnauthorizedAccessException("denied"));
        var database = new AppDatabase();
        database.GetOrCreateAccount(TargetSid.Value).Grants.Add(new GrantedPathEntry
        {
            Path = Path.GetFullPath(file1),
            IsDeny = false,
            SavedRights = SavedRightsState.DefaultForMode(isDeny: false)
        });
        var handler = CreateHandler();

        var (result, _, granted, _) = await handler.ResolveFileAccessAsync(TargetSid, null, [file1, file2], SourceSid, null, database, CancellationToken.None);

        Assert.Null(result);
        Assert.Empty(granted);
        _pathGrantService.Verify(g => g.RestoreTraverse(
            TargetSid.Value,
            Path.GetDirectoryName(Path.GetFullPath(file1))!,
            It.Is<GrantIntentRestoreSnapshot>(snapshot => snapshot.RuntimeEntry == null && snapshot.Locations.Count == 0)), Times.Once);
        _pathGrantService.Verify(g => g.RestoreGrant(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<GrantIntentRestoreSnapshot>()), Times.Once);
    }
}
