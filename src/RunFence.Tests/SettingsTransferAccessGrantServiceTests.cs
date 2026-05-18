using System.Security.AccessControl;
using Moq;
using RunFence.Acl;
using RunFence.Core;
using RunFence.Persistence;
using RunFence.PrefTrans;
using Xunit;

namespace RunFence.Tests;

public class SettingsTransferAccessGrantServiceTests
{
    private const string TestSid = "S-1-5-21-1111-1111-1111-1111";
    private readonly Mock<IPathGrantService> _pathGrantService;
    private readonly Mock<IGrantAceService> _grantAceService;
    private readonly Mock<ILoggingService> _log;
    private readonly ISettingsTransferAccessGrantService _service;

    public SettingsTransferAccessGrantServiceTests()
    {
        _pathGrantService = new Mock<IPathGrantService>();
        _pathGrantService
            .Setup(g => g.CaptureGrantRestoreSnapshot(It.IsAny<string>(), It.IsAny<string>(), false))
            .Returns(new GrantIntentRestoreSnapshot(null, []));
        _pathGrantService
            .Setup(g => g.CaptureTraverseRestoreSnapshot(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new GrantIntentRestoreSnapshot(null, []));
        _pathGrantService
            .Setup(g => g.EnsureAccess(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<FileSystemRights>(),
                It.IsAny<Func<string, string, bool>?>(),
                It.IsAny<bool>()))
            .Returns(new GrantApplyResult());
        _pathGrantService
            .Setup(g => g.EnsureTemporaryAccess(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<FileSystemRights>(),
                It.IsAny<Func<string, string, bool>?>(),
                It.IsAny<bool>()))
            .Returns(new GrantApplyResult());
        _pathGrantService
            .Setup(g => g.RestoreGrant(It.IsAny<string>(), It.IsAny<string>(), false, It.IsAny<GrantIntentRestoreSnapshot>()))
            .Returns(new GrantApplyResult());
        _pathGrantService
            .Setup(g => g.RestoreTraverse(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GrantIntentRestoreSnapshot>()))
            .Returns(new GrantApplyResult());

        _grantAceService = new Mock<IGrantAceService>();
        _log = new Mock<ILoggingService>();
        _service = new SettingsTransferAccessGrantService(
            _pathGrantService.Object,
            _grantAceService.Object,
            _log.Object);
    }

    [Fact]
    public void TryEnsureDurableAccess_DoesNotRecordCleanupState()
    {
        var path = "C:\\temp\\preftrans";
        var normalizedPath = Path.GetFullPath(path);

        _pathGrantService
            .Setup(g => g.EnsureAccess(TestSid, normalizedPath, FileSystemRights.ReadAndExecute, null, false))
            .Returns(new GrantApplyResult(GrantApplied: true, DatabaseModified: true));

        var result = _service.TryEnsureDurableAccess(TestSid, path, FileSystemRights.ReadAndExecute);

        Assert.True(result.Succeeded);
        Assert.True(result.GrantCreated);

        _service.CleanupTemporaryGrant();

        _pathGrantService.Verify(
            g => g.CaptureGrantRestoreSnapshot(It.IsAny<string>(), It.IsAny<string>(), false),
            Times.Never);
        _pathGrantService.Verify(
            g => g.CaptureTraverseRestoreSnapshot(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
        _pathGrantService.Verify(
            g => g.RestoreGrant(It.IsAny<string>(), It.IsAny<string>(), false, It.IsAny<GrantIntentRestoreSnapshot>()),
            Times.Never);
        _pathGrantService.Verify(
            g => g.RestoreTraverse(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GrantIntentRestoreSnapshot>()),
            Times.Never);
    }

    [Fact]
    public void TryEnsureAccess_RecordsCleanupStateAndRestoresGrantAndTraverse()
    {
        var path = "C:\\temp\\transfer.txt";
        var normalizedPath = Path.GetFullPath(path);
        var traversePath = Path.GetDirectoryName(normalizedPath)!;
        var grantSnapshot = new GrantIntentRestoreSnapshot(null, []);
        var traverseSnapshot = new GrantIntentRestoreSnapshot(null, []);

        _pathGrantService
            .Setup(g => g.CaptureGrantRestoreSnapshot(TestSid, normalizedPath, false))
            .Returns(grantSnapshot);
        _pathGrantService
            .Setup(g => g.CaptureTraverseRestoreSnapshot(TestSid, traversePath))
            .Returns(traverseSnapshot);
        _pathGrantService
            .Setup(g => g.EnsureAccess(TestSid, normalizedPath, FileSystemRights.Read, null, false))
            .Returns(new GrantApplyResult(GrantApplied: true, DatabaseModified: true));

        var result = _service.TryEnsureAccess(TestSid, path, FileSystemRights.Read, isDirectory: false);

        Assert.True(result.Succeeded);
        Assert.True(result.GrantCreated);

        _service.CleanupTemporaryGrant();

        _pathGrantService.Verify(
            g => g.RestoreGrant(TestSid, normalizedPath, false, grantSnapshot),
            Times.Once);
        _pathGrantService.Verify(
            g => g.RestoreTraverse(TestSid, traversePath, traverseSnapshot),
            Times.Once);
    }

    [Fact]
    public void TryEnsureAccess_WhenOnlyAclRepairWasNeeded_DoesNotTrackCleanup()
    {
        var path = "C:\\temp\\existing.txt";
        var normalizedPath = Path.GetFullPath(path);

        _pathGrantService
            .Setup(g => g.EnsureAccess(TestSid, normalizedPath, FileSystemRights.Read, null, false))
            .Returns(new GrantApplyResult(GrantApplied: true, DatabaseModified: false));

        var result = _service.TryEnsureAccess(TestSid, path, FileSystemRights.Read, isDirectory: false);

        Assert.True(result.Succeeded);
        Assert.False(result.GrantCreated);

        _service.CleanupTemporaryGrant();

        _pathGrantService.Verify(
            g => g.RestoreGrant(It.IsAny<string>(), It.IsAny<string>(), false, It.IsAny<GrantIntentRestoreSnapshot>()),
            Times.Never);
        _pathGrantService.Verify(
            g => g.RestoreTraverse(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<GrantIntentRestoreSnapshot>()),
            Times.Never);
    }

    [Fact]
    public void TryEnsureAccessForCleanup_UsesTemporaryAccessAndDoesNotReplaceExistingTransferCleanupState()
    {
        var transferPath = "C:\\temp\\preftrans";
        var normalizedTransferPath = Path.GetFullPath(transferPath);
        var cleanupOnlyPath = "C:\\temp\\staging\\settings.json";
        var normalizedCleanupOnlyPath = Path.GetFullPath(cleanupOnlyPath);
        var transferTraversePath = normalizedTransferPath;
        var cleanupTraversePath = Path.GetDirectoryName(normalizedCleanupOnlyPath)!;
        var grantSnapshot = new GrantIntentRestoreSnapshot(null, []);
        var traverseSnapshot = new GrantIntentRestoreSnapshot(null, []);
        var cleanupTraverseSnapshot = new GrantIntentRestoreSnapshot(null, []);

        _pathGrantService
            .Setup(g => g.CaptureGrantRestoreSnapshot(TestSid, normalizedTransferPath, false))
            .Returns(grantSnapshot);
        _pathGrantService
            .Setup(g => g.CaptureTraverseRestoreSnapshot(TestSid, transferTraversePath))
            .Returns(traverseSnapshot);
        _pathGrantService
            .Setup(g => g.CaptureTraverseRestoreSnapshot(TestSid, cleanupTraversePath))
            .Returns(cleanupTraverseSnapshot);
        _pathGrantService
            .Setup(g => g.EnsureAccess(TestSid, normalizedTransferPath, FileSystemRights.ReadAndExecute, null, false))
            .Returns(new GrantApplyResult(GrantApplied: true, DatabaseModified: true));
        _pathGrantService
            .Setup(g => g.EnsureTemporaryAccess(
                TestSid,
                normalizedCleanupOnlyPath,
                FileSystemRights.Read | FileSystemRights.Synchronize,
                null,
                false))
            .Returns(new GrantApplyResult(GrantApplied: true));

        _service.TryEnsureAccess(TestSid, transferPath, FileSystemRights.ReadAndExecute, isDirectory: true);

        var cleanupResult = _service.TryEnsureAccessForCleanup(
            TestSid,
            cleanupOnlyPath,
            FileSystemRights.Read | FileSystemRights.Synchronize,
            isDirectory: false);

        Assert.True(cleanupResult.Succeeded);
        Assert.True(cleanupResult.GrantCreated);

        _service.CleanupTemporaryGrant();

        _pathGrantService.Verify(
            g => g.EnsureTemporaryAccess(
                TestSid,
                normalizedCleanupOnlyPath,
                FileSystemRights.Read | FileSystemRights.Synchronize,
                null,
                false),
            Times.Once);
        _pathGrantService.Verify(
            g => g.RestoreGrant(TestSid, normalizedTransferPath, false, grantSnapshot),
            Times.Once);
        _pathGrantService.Verify(
            g => g.RestoreGrant(TestSid, normalizedCleanupOnlyPath, false, It.IsAny<GrantIntentRestoreSnapshot>()),
            Times.Never);
        _grantAceService.Verify(
            g => g.RevertAce(normalizedCleanupOnlyPath, TestSid, false),
            Times.Once);
        _pathGrantService.Verify(
            g => g.RestoreTraverse(TestSid, cleanupTraversePath, cleanupTraverseSnapshot),
            Times.Once);
    }

    [Fact]
    public void TryEnsureAccessForCleanup_WhenItCreatesGrant_TracksCleanupEvenWithoutTransferGrant()
    {
        var cleanupOnlyPath = "C:\\temp\\staging\\settings.json";
        var normalizedCleanupOnlyPath = Path.GetFullPath(cleanupOnlyPath);
        var cleanupTraversePath = Path.GetDirectoryName(normalizedCleanupOnlyPath)!;
        var cleanupTraverseSnapshot = new GrantIntentRestoreSnapshot(null, []);

        _pathGrantService
            .Setup(g => g.CaptureTraverseRestoreSnapshot(TestSid, cleanupTraversePath))
            .Returns(cleanupTraverseSnapshot);
        _pathGrantService
            .Setup(g => g.EnsureTemporaryAccess(
                TestSid,
                normalizedCleanupOnlyPath,
                FileSystemRights.Read | FileSystemRights.Synchronize,
                null,
                false))
            .Returns(new GrantApplyResult(GrantApplied: true));

        var result = _service.TryEnsureAccessForCleanup(
            TestSid,
            cleanupOnlyPath,
            FileSystemRights.Read | FileSystemRights.Synchronize,
            isDirectory: false);

        Assert.True(result.Succeeded);
        Assert.True(result.GrantCreated);

        _service.CleanupTemporaryGrant();

        _pathGrantService.Verify(
            g => g.RestoreGrant(It.IsAny<string>(), It.IsAny<string>(), false, It.IsAny<GrantIntentRestoreSnapshot>()),
            Times.Never);
        _grantAceService.Verify(
            g => g.RevertAce(normalizedCleanupOnlyPath, TestSid, false),
            Times.Once);
        _pathGrantService.Verify(
            g => g.RestoreTraverse(TestSid, cleanupTraversePath, cleanupTraverseSnapshot),
            Times.Once);
    }

    [Fact]
    public void TryEnsureAccessForCleanup_WhenOnlyTraverseWasAdded_TracksCleanupAndReportsGrantCreated()
    {
        var cleanupOnlyPath = "C:\\temp\\staging\\settings.json";
        var normalizedCleanupOnlyPath = Path.GetFullPath(cleanupOnlyPath);
        var cleanupTraversePath = Path.GetDirectoryName(normalizedCleanupOnlyPath)!;
        var cleanupTraverseSnapshot = new GrantIntentRestoreSnapshot(null, []);

        _pathGrantService
            .Setup(g => g.CaptureTraverseRestoreSnapshot(TestSid, cleanupTraversePath))
            .Returns(cleanupTraverseSnapshot);
        _pathGrantService
            .Setup(g => g.EnsureTemporaryAccess(
                TestSid,
                normalizedCleanupOnlyPath,
                FileSystemRights.Read | FileSystemRights.Synchronize,
                null,
                false))
            .Returns(new GrantApplyResult(TraverseApplied: true));

        var result = _service.TryEnsureAccessForCleanup(
            TestSid,
            cleanupOnlyPath,
            FileSystemRights.Read | FileSystemRights.Synchronize,
            isDirectory: false);

        Assert.True(result.Succeeded);
        Assert.True(result.GrantCreated);

        _service.CleanupTemporaryGrant();

        _pathGrantService.Verify(
            g => g.RestoreGrant(It.IsAny<string>(), It.IsAny<string>(), false, It.IsAny<GrantIntentRestoreSnapshot>()),
            Times.Never);
        _grantAceService.Verify(
            g => g.RevertAce(It.IsAny<string>(), It.IsAny<string>(), false),
            Times.Never);
        _pathGrantService.Verify(
            g => g.RestoreTraverse(TestSid, cleanupTraversePath, cleanupTraverseSnapshot),
            Times.Once);
    }

    [Fact]
    public void TryEnsureAccess_ReturnsWarningResultWhenPermissionCheckFails()
    {
        var path = "C:\\temp\\transfer.txt";
        var normalizedPath = Path.GetFullPath(path);

        _pathGrantService
            .Setup(g => g.CaptureGrantRestoreSnapshot(TestSid, normalizedPath, false))
            .Throws(new InvalidOperationException("access check failed"));

        var result = _service.TryEnsureAccess(TestSid, path, FileSystemRights.Read, isDirectory: false);

        Assert.False(result.Succeeded);
        Assert.False(result.GrantCreated);
        Assert.Equal("access check failed", result.WarningMessage);
    }

    [Fact]
    public void CleanupTemporaryGrant_SwallowsRestoreFailures()
    {
        var path = "C:\\temp\\transfer.txt";
        var normalizedPath = Path.GetFullPath(path);
        var traversePath = Path.GetDirectoryName(normalizedPath)!;
        var grantSnapshot = new GrantIntentRestoreSnapshot(null, []);
        var traverseSnapshot = new GrantIntentRestoreSnapshot(null, []);

        _pathGrantService
            .Setup(g => g.CaptureGrantRestoreSnapshot(TestSid, normalizedPath, false))
            .Returns(grantSnapshot);
        _pathGrantService
            .Setup(g => g.CaptureTraverseRestoreSnapshot(TestSid, traversePath))
            .Returns(traverseSnapshot);
        _pathGrantService
            .Setup(g => g.EnsureAccess(TestSid, normalizedPath, FileSystemRights.Read, null, false))
            .Returns(new GrantApplyResult(DatabaseModified: true));
        _pathGrantService
            .Setup(g => g.RestoreGrant(TestSid, normalizedPath, false, grantSnapshot))
            .Throws(new InvalidOperationException("grant cleanup failed"));
        _pathGrantService
            .Setup(g => g.RestoreTraverse(TestSid, traversePath, traverseSnapshot))
            .Throws(new InvalidOperationException("traverse cleanup failed"));

        _service.TryEnsureAccess(TestSid, path, FileSystemRights.Read, isDirectory: false);

        _service.CleanupTemporaryGrant();

        _log.Verify(l => l.Warn(It.Is<string>(m => m.Contains("grant cleanup failed"))), Times.Once);
        _log.Verify(l => l.Warn(It.Is<string>(m => m.Contains("traverse cleanup failed"))), Times.Once);
    }

    [Fact]
    public void CleanupTemporaryGrant_SwallowsTemporaryAceCleanupFailures()
    {
        var cleanupOnlyPath = "C:\\temp\\staging\\settings.json";
        var normalizedCleanupOnlyPath = Path.GetFullPath(cleanupOnlyPath);
        var cleanupTraversePath = Path.GetDirectoryName(normalizedCleanupOnlyPath)!;
        var cleanupTraverseSnapshot = new GrantIntentRestoreSnapshot(null, []);

        _pathGrantService
            .Setup(g => g.CaptureTraverseRestoreSnapshot(TestSid, cleanupTraversePath))
            .Returns(cleanupTraverseSnapshot);
        _pathGrantService
            .Setup(g => g.EnsureTemporaryAccess(
                TestSid,
                normalizedCleanupOnlyPath,
                FileSystemRights.Read | FileSystemRights.Synchronize,
                null,
                false))
            .Returns(new GrantApplyResult(GrantApplied: true));
        _grantAceService
            .Setup(g => g.RevertAce(normalizedCleanupOnlyPath, TestSid, false))
            .Throws(new InvalidOperationException("temporary ace cleanup failed"));

        _service.TryEnsureAccessForCleanup(
            TestSid,
            cleanupOnlyPath,
            FileSystemRights.Read | FileSystemRights.Synchronize,
            isDirectory: false);

        _service.CleanupTemporaryGrant();

        _log.Verify(l => l.Warn(It.Is<string>(m => m.Contains("temporary ace cleanup failed"))), Times.Once);
    }
}
