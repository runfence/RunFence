using RunFence.Account.UI;
using Xunit;

namespace RunFence.Tests;

public sealed class WindowsTerminalPackageCacheServiceTests : IDisposable
{
    private readonly TempDirectory _tempDirectory = new("RunFence_WindowsTerminalPackageCache");

    [Fact]
    public async Task EnsureCachedZipAsync_WhenLatestZipMissing_DownloadsToTempThenMovesToFinal()
    {
        var paths = CreatePaths();
        var runner = new FakeWindowsTerminalDeploymentConsoleRunner();
        var verifier = new FakeWindowsTerminalPackageVerifier();
        var service = new WindowsTerminalPackageCacheService(paths, runner, verifier);
        var release = CreateRelease(new Version(1, 2, 3, 4));

        var cachedZipPath = await service.EnsureCachedZipAsync(
            release,
            WindowsTerminalReleaseClient.GetArchitectureSuffix(),
            CancellationToken.None);

        Assert.Equal(paths.GetCachedZipPath(release.Version, WindowsTerminalReleaseClient.GetArchitectureSuffix()), cachedZipPath);
        Assert.Equal(1, runner.DownloadCallCount);
        Assert.StartsWith(cachedZipPath + ".", runner.LastDownloadDestinationPath, StringComparison.Ordinal);
        Assert.True(File.Exists(cachedZipPath));
        Assert.False(File.Exists(runner.LastDownloadDestinationPath!));
    }

    [Fact]
    public async Task EnsureCachedZipAsync_WhenExistingTargetIsValid_DoesNotDownload()
    {
        var paths = CreatePaths();
        var release = CreateRelease(new Version(1, 2, 3, 4));
        var cachedZipPath = paths.GetCachedZipPath(release.Version, WindowsTerminalReleaseClient.GetArchitectureSuffix());
        Directory.CreateDirectory(paths.DownloadCacheDirectoryPath);
        File.WriteAllText(cachedZipPath, "zip");
        var runner = new FakeWindowsTerminalDeploymentConsoleRunner();
        var verifier = new FakeWindowsTerminalPackageVerifier();
        var service = new WindowsTerminalPackageCacheService(paths, runner, verifier);

        var result = await service.EnsureCachedZipAsync(
            release,
            WindowsTerminalReleaseClient.GetArchitectureSuffix(),
            CancellationToken.None);

        Assert.Equal(cachedZipPath, result);
        Assert.Equal(0, runner.DownloadCallCount);
        Assert.Contains(cachedZipPath, verifier.VerifiedPaths);
    }

    [Fact]
    public async Task EnsureCachedZipAsync_WhenExistingTargetIsInvalid_DeletesAndRedownloads()
    {
        var paths = CreatePaths();
        var release = CreateRelease(new Version(1, 2, 3, 4));
        var cachedZipPath = paths.GetCachedZipPath(release.Version, WindowsTerminalReleaseClient.GetArchitectureSuffix());
        Directory.CreateDirectory(paths.DownloadCacheDirectoryPath);
        File.WriteAllText(cachedZipPath, "bad");
        var runner = new FakeWindowsTerminalDeploymentConsoleRunner();
        var verifier = new FakeWindowsTerminalPackageVerifier();
        verifier.RejectPath(cachedZipPath);
        var service = new WindowsTerminalPackageCacheService(paths, runner, verifier);

        var result = await service.EnsureCachedZipAsync(
            release,
            WindowsTerminalReleaseClient.GetArchitectureSuffix(),
            CancellationToken.None);

        Assert.Equal(cachedZipPath, result);
        Assert.Equal(1, runner.DownloadCallCount);
        Assert.True(File.Exists(cachedZipPath));
        Assert.Contains(cachedZipPath, verifier.VerifiedPaths);
        Assert.Contains(runner.LastDownloadDestinationPath!, verifier.VerifiedPaths);
    }

    [Fact]
    public async Task EnsureCachedZipAsync_WhenDownloadedZipIsInvalid_ThrowsAndLeavesNoFinalOrTempFile()
    {
        var paths = CreatePaths();
        var release = CreateRelease(new Version(1, 2, 3, 4));
        var runner = new FakeWindowsTerminalDeploymentConsoleRunner();
        var verifier = new FakeWindowsTerminalPackageVerifier { RejectAll = true };
        var service = new WindowsTerminalPackageCacheService(paths, runner, verifier);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnsureCachedZipAsync(
            release,
            WindowsTerminalReleaseClient.GetArchitectureSuffix(),
            CancellationToken.None));

        var cachedZipPath = paths.GetCachedZipPath(release.Version, WindowsTerminalReleaseClient.GetArchitectureSuffix());
        Assert.Contains("signature verification", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(cachedZipPath));
        Assert.False(File.Exists(runner.LastDownloadDestinationPath!));
    }

    [Fact]
    public void TryGetVerifiedLatestCachedZip_WhenNewestCachedZipIsInvalid_DoesNotFallBackToOlderZip()
    {
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.DownloadCacheDirectoryPath);
        var olderPath = paths.GetCachedZipPath(new Version(1, 2, 3, 3), WindowsTerminalReleaseClient.GetArchitectureSuffix());
        var newerPath = paths.GetCachedZipPath(new Version(1, 2, 3, 4), WindowsTerminalReleaseClient.GetArchitectureSuffix());
        File.WriteAllText(olderPath, "older");
        File.WriteAllText(newerPath, "newer");
        var verifier = new FakeWindowsTerminalPackageVerifier();
        verifier.RejectPath(newerPath);
        var service = new WindowsTerminalPackageCacheService(
            paths,
            new FakeWindowsTerminalDeploymentConsoleRunner(),
            verifier);

        var found = service.TryGetVerifiedLatestCachedZip(
            WindowsTerminalReleaseClient.GetArchitectureSuffix(),
            out var version,
            out var path);

        Assert.False(found);
        Assert.Null(version);
        Assert.Null(path);
        Assert.False(File.Exists(newerPath));
        Assert.True(File.Exists(olderPath));
        Assert.Equal([newerPath], verifier.VerifiedPaths);
    }

    public void Dispose()
    {
        _tempDirectory.Dispose();
    }

    private WindowsTerminalDeploymentPaths CreatePaths()
        => new(new TestProgramDataKnownPathResolver(_tempDirectory.Path));

    private static WindowsTerminalReleaseInfo CreateRelease(Version version)
    {
        var architecture = WindowsTerminalReleaseClient.GetArchitectureSuffix();
        return new WindowsTerminalReleaseInfo(
            version,
            $"Microsoft.WindowsTerminal_{version}_{architecture}.zip",
            $"https://example.invalid/Microsoft.WindowsTerminal_{version}_{architecture}.zip");
    }

    private sealed class FakeWindowsTerminalPackageVerifier : IWindowsTerminalPackageVerifier
    {
        private readonly HashSet<string> rejectedPaths = new(StringComparer.OrdinalIgnoreCase);

        public List<string> VerifiedPaths { get; } = [];
        public bool RejectAll { get; init; }

        public void RejectPath(string path) => rejectedPaths.Add(path);

        public void VerifyPackage(string zipPath)
        {
            VerifiedPaths.Add(zipPath);
            if (RejectAll || rejectedPaths.Contains(zipPath))
                throw new InvalidOperationException("Package verification failed.");
        }
    }

    private sealed class FakeWindowsTerminalDeploymentConsoleRunner : IWindowsTerminalDeploymentConsoleRunner
    {
        public int DownloadCallCount { get; private set; }
        public string? LastDownloadDestinationPath { get; private set; }

        public Task DownloadAsync(WindowsTerminalPackageDownloadOperation operation, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DownloadCallCount++;
            LastDownloadDestinationPath = operation.DestinationPath;
            Directory.CreateDirectory(Path.GetDirectoryName(operation.DestinationPath)!);
            File.WriteAllText(operation.DestinationPath, "zip");
            return Task.CompletedTask;
        }

        public Task RunAsync(WindowsTerminalDeploymentOperation operation, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
