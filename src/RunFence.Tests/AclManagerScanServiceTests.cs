using Moq;
using RunFence.Acl;
using RunFence.Core;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Tests for <see cref="AclManagerScanService"/> thin-enumerator behavior.
/// <para>
/// The actual file enumeration and ACE application requires an elevated process,
/// so these tests verify the contract: UpdateFromPath is called per discovered path,
/// progress is reported, and cancellation is honored.
/// </para>
/// </summary>
public class AclManagerScanServiceTests
{
    private readonly Mock<IPathGrantService> _pathGrantService = new();
    private readonly Mock<ILoggingService> _log = new();

    private AclManagerScanService CreateService() =>
        new(_pathGrantService.Object, _log.Object);

    [Fact]
    public async Task ScanAsync_EmptyFolder_CallsUpdateFromPathForRootAndAncestors()
    {
        using var tempDir = new TempDirectory("rftest_scan");
        var service = CreateService();
        var progress = new Progress<long>();

        await service.ScanAsync(tempDir.Path, "S-1-5-21-0-0-0-1000", progress, CancellationToken.None);

        // Root should be processed, plus ancestors up to drive root.
        _pathGrantService.Verify(
            s => s.UpdateFromPath(tempDir.Path, "S-1-5-21-0-0-0-1000"),
            Times.Once);
    }

    [Fact]
    public async Task ScanAsync_FolderWithFiles_CallsUpdateFromPathForEachEntry()
    {
        using var tempDir = new TempDirectory("rftest_scan");
        var fileA = Path.Combine(tempDir.Path, "a.txt");
        var fileB = Path.Combine(tempDir.Path, "b.txt");
        File.WriteAllText(fileA, "");
        File.WriteAllText(fileB, "");

        var service = CreateService();
        var progress = new Progress<long>();

        await service.ScanAsync(tempDir.Path, "S-1-5-21-0-0-0-1000", progress, CancellationToken.None);

        _pathGrantService.Verify(
            s => s.UpdateFromPath(fileA, "S-1-5-21-0-0-0-1000"),
            Times.Once);
        _pathGrantService.Verify(
            s => s.UpdateFromPath(fileB, "S-1-5-21-0-0-0-1000"),
            Times.Once);
    }

    [Fact]
    public async Task ScanAsync_FolderWithSubdir_CallsUpdateFromPathForSubdir()
    {
        using var tempDir = new TempDirectory("rftest_scan");
        var subDir = Directory.CreateDirectory(Path.Combine(tempDir.Path, "sub")).FullName;

        var service = CreateService();
        var progress = new Progress<long>();

        await service.ScanAsync(tempDir.Path, "S-1-5-21-0-0-0-1000", progress, CancellationToken.None);

        _pathGrantService.Verify(
            s => s.UpdateFromPath(subDir, "S-1-5-21-0-0-0-1000"),
            Times.Once);
    }

    [Fact]
    public async Task ScanAsync_UpdateFromPathReturnsTrue_CountsAsUpdated()
    {
        using var tempDir = new TempDirectory("rftest_scan");
        _pathGrantService.Setup(s => s.UpdateFromPath(It.IsAny<string>(), It.IsAny<string?>()))
            .Returns(true);

        var service = CreateService();
        var progress = new Progress<long>();

        var updated = await service.ScanAsync(tempDir.Path, "S-1-5-21-0-0-0-1000", progress, CancellationToken.None);

        Assert.True(updated > 0);
    }

    [Fact]
    public async Task ScanAsync_CancellationRequested_StopsEarly()
    {
        using var tempDir = new TempDirectory("rftest_scan");
        // Create many files so cancellation has a chance to trigger mid-scan.
        for (int i = 0; i < 20; i++)
            File.WriteAllText(Path.Combine(tempDir.Path, $"file{i}.txt"), "");

        var cts = new CancellationTokenSource();
        cts.Cancel(); // cancel immediately

        var service = CreateService();
        var progress = new Progress<long>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.ScanAsync(tempDir.Path, "S-1-5-21-0-0-0-1000", progress, cts.Token));
    }

    [Fact]
    public async Task ScanAsync_WalksAncestors_UpdateFromPathCalledForParentDirectories()
    {
        using var tempDir = new TempDirectory("rftest_scan");
        var subDir = Directory.CreateDirectory(Path.Combine(tempDir.Path, "level1", "level2")).FullName;

        var service = CreateService();
        var progress = new Progress<long>();

        await service.ScanAsync(subDir, "S-1-5-21-0-0-0-1000", progress, CancellationToken.None);

        // tempDir is an ancestor of subDir and should be visited.
        _pathGrantService.Verify(
            s => s.UpdateFromPath(tempDir.Path, "S-1-5-21-0-0-0-1000"),
            Times.Once);
    }

    [Fact]
    public async Task ScanAsync_ProgressReported_AtLeastOnce()
    {
        using var tempDir = new TempDirectory("rftest_scan");
        File.WriteAllText(Path.Combine(tempDir.Path, "file.txt"), "");

        var reported = new List<long>();
        var progress = new SynchronousProgress<long>(n => reported.Add(n));

        var service = CreateService();
        await service.ScanAsync(tempDir.Path, "S-1-5-21-0-0-0-1000", progress, CancellationToken.None);

        Assert.NotEmpty(reported);
    }

    private sealed class SynchronousProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}
