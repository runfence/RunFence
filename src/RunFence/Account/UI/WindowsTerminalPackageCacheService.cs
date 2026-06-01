namespace RunFence.Account.UI;

public interface IWindowsTerminalPackageCacheService
{
    Task<string> EnsureCachedZipAsync(
        WindowsTerminalReleaseInfo release,
        string architecture,
        CancellationToken cancellationToken);

    bool TryGetVerifiedLatestCachedZip(string architecture, out Version? version, out string? path);
    bool TryVerifyCachedZip(string cachedZipPath);
}

public sealed class WindowsTerminalPackageCacheService(
    WindowsTerminalDeploymentPaths deploymentPaths,
    IWindowsTerminalDeploymentConsoleRunner deploymentConsoleRunner,
    IWindowsTerminalPackageVerifier packageVerifier)
    : IWindowsTerminalPackageCacheService
{
    private readonly SemaphoreSlim _cacheGate = new(1, 1);

    public async Task<string> EnsureCachedZipAsync(
        WindowsTerminalReleaseInfo release,
        string architecture,
        CancellationToken cancellationToken)
    {
        var cachedZipPath = deploymentPaths.GetCachedZipPath(release.Version, architecture);
        if (File.Exists(cachedZipPath) && TryVerifyCachedZip(cachedZipPath))
            return cachedZipPath;

        await _cacheGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(cachedZipPath) && TryVerifyCachedZip(cachedZipPath))
                return cachedZipPath;

            var tempZipPath = cachedZipPath + $".{Guid.NewGuid():N}.tmp";
            try
            {
                await deploymentConsoleRunner.DownloadAsync(
                        new WindowsTerminalPackageDownloadOperation(release.DownloadUrl, tempZipPath),
                        cancellationToken)
                    .ConfigureAwait(false);
                VerifyDownloadedZip(tempZipPath);
                File.Move(tempZipPath, cachedZipPath, overwrite: true);
            }
            finally
            {
                TryDeleteFile(tempZipPath);
            }

            return cachedZipPath;
        }
        finally
        {
            _cacheGate.Release();
        }
    }

    public bool TryGetVerifiedLatestCachedZip(string architecture, out Version? version, out string? path)
    {
        version = null;
        path = null;
        if (!Directory.Exists(deploymentPaths.DownloadCacheDirectoryPath))
            return false;

        foreach (var candidatePath in Directory.GetFiles(
                     deploymentPaths.DownloadCacheDirectoryPath,
                     $"Microsoft.WindowsTerminal_*_{architecture}.zip"))
        {
            if (!WindowsTerminalDeploymentPaths.TryParseCachedZipVersion(candidatePath, architecture, out var candidateVersion))
                continue;

            if (version == null || candidateVersion > version)
            {
                version = candidateVersion;
                path = candidatePath;
            }
        }

        if (version == null || path == null)
            return false;

        if (TryVerifyCachedZip(path))
            return true;

        version = null;
        path = null;
        return false;
    }

    public bool TryVerifyCachedZip(string cachedZipPath)
    {
        try
        {
            packageVerifier.VerifyPackage(cachedZipPath);
            return true;
        }
        catch (Exception)
        {
            TryDeleteFile(cachedZipPath);
            return false;
        }
    }

    private void VerifyDownloadedZip(string tempZipPath)
    {
        try
        {
            packageVerifier.VerifyPackage(tempZipPath);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "Downloaded Windows Terminal ZIP failed signature verification.",
                exception);
        }
    }

    private void TryDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
        catch (Exception)
        {
        }
    }
}
