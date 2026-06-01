using System.Threading;
using RunFence.Acl;

namespace RunFence.Account.UI;

public interface IWindowsTerminalDeploymentService
{
    Task EnsureSharedDeploymentReadyAsync(CancellationToken cancellationToken);
    Task EnsureLatestReleaseCachedAsync(CancellationToken cancellationToken);
    Task<bool> TryDeployLatestCachedZipIfNewerThanSharedAsync(CancellationToken cancellationToken);
}

public sealed class WindowsTerminalDeploymentService(
    IProgramDataDirectoryProvisioningService programDataDirectoryProvisioningService,
    IProgramDataPathPolicyService programDataPathPolicyService,
    WindowsTerminalDeploymentPaths deploymentPaths,
    IWindowsTerminalReleaseClient releaseClient,
    IWindowsTerminalPackageCacheService packageCacheService,
    IWindowsTerminalSharedDeploymentSecurityService sharedDeploymentSecurityService,
    IWindowsTerminalDeploymentConsoleRunner deploymentConsoleRunner,
    WindowsTerminalDeploymentDirectoryCleaner deploymentDirectoryCleaner)
    : IWindowsTerminalDeploymentService
{
    private readonly SemaphoreSlim _deploymentGate = new(1, 1);

    public async Task EnsureSharedDeploymentReadyAsync(CancellationToken cancellationToken)
    {
        await _deploymentGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureSharedDeploymentReadyCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _deploymentGate.Release();
        }
    }

    public async Task<bool> TryDeployLatestCachedZipIfNewerThanSharedAsync(CancellationToken cancellationToken)
    {
        await _deploymentGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await TryDeployLatestCachedZipIfNewerThanSharedCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _deploymentGate.Release();
        }
    }

    public Task EnsureLatestReleaseCachedAsync(CancellationToken cancellationToken)
        => EnsureLatestReleaseCachedCoreAsync(cancellationToken);

    private async Task EnsureSharedDeploymentReadyCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureProgramDataRootAndCache();
        RecoverInterruptedDeployment();
        sharedDeploymentSecurityService.EnsureSharedDeploymentDirectory();

        if (File.Exists(deploymentPaths.SharedExecutablePath) && IsDeploymentLocked())
            return;

        var architecture = WindowsTerminalReleaseClient.GetArchitectureSuffix();
        var latestRelease = await releaseClient.GetLatestReleaseAsync(cancellationToken).ConfigureAwait(false);
        if (IsSharedDeploymentVersion(latestRelease.Version))
        {
            var copiedPrivilegeSpecificExecutables = sharedDeploymentSecurityService.EnsureExecutableCopies();
            var helperFilesChanged = sharedDeploymentSecurityService.EnsureHelperFiles();
            sharedDeploymentSecurityService.EnsureSharedDeploymentTreeSecurity();
            if (copiedPrivilegeSpecificExecutables || helperFilesChanged)
            {
                sharedDeploymentSecurityService.EnsureSharedDeploymentTreeSecurity();
            }

            return;
        }

        var cachedZipPath = deploymentPaths.GetCachedZipPath(latestRelease.Version, architecture);
        if (!packageCacheService.TryGetVerifiedLatestCachedZip(architecture, out var cachedVersion, out var existingCachedZipPath) ||
            cachedVersion == null ||
            latestRelease.Version != cachedVersion ||
            existingCachedZipPath == null)
        {
            cachedZipPath = await packageCacheService
                .EnsureCachedZipAsync(latestRelease, architecture, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            cachedZipPath = existingCachedZipPath;
        }

        await DeployCachedZipAsync(cachedZipPath, latestRelease.Version, cancellationToken).ConfigureAwait(false);

        if (!IsSharedDeploymentVersion(latestRelease.Version))
        {
            throw new InvalidOperationException(
                $"Shared Windows Terminal deployment did not produce version {latestRelease.Version}.");
        }

        sharedDeploymentSecurityService.EnsureSharedDeploymentTreeSecurity();
        if (sharedDeploymentSecurityService.EnsureHelperFiles())
        {
            sharedDeploymentSecurityService.EnsureSharedDeploymentTreeSecurity();
        }
    }

    private async Task<bool> TryDeployLatestCachedZipIfNewerThanSharedCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureProgramDataRootAndCache();
        RecoverInterruptedDeployment();
        sharedDeploymentSecurityService.EnsureSharedDeploymentDirectory();

        if (File.Exists(deploymentPaths.SharedExecutablePath) && IsDeploymentLocked())
            return false;

        var architecture = WindowsTerminalReleaseClient.GetArchitectureSuffix();
        if (!packageCacheService.TryGetVerifiedLatestCachedZip(architecture, out var cachedVersion, out var cachedZipPath) ||
            cachedVersion == null ||
            cachedZipPath == null)
        {
            return false;
        }

        if (TryReadSharedDeploymentVersion(out var deployedVersion) && cachedVersion <= deployedVersion)
            return false;

        await DeployCachedZipAsync(cachedZipPath, cachedVersion, cancellationToken).ConfigureAwait(false);

        if (!IsSharedDeploymentVersion(cachedVersion))
        {
            throw new InvalidOperationException(
                $"Shared Windows Terminal cached deployment did not produce version {cachedVersion}.");
        }

        sharedDeploymentSecurityService.EnsureSharedDeploymentTreeSecurity();
        if (sharedDeploymentSecurityService.EnsureHelperFiles())
        {
            sharedDeploymentSecurityService.EnsureSharedDeploymentTreeSecurity();
        }
        return true;
    }

    private async Task EnsureLatestReleaseCachedCoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureProgramDataRootAndCache();

        var architecture = WindowsTerminalReleaseClient.GetArchitectureSuffix();
        var latestRelease = await releaseClient.GetLatestReleaseAsync(cancellationToken).ConfigureAwait(false);
        await packageCacheService.EnsureCachedZipAsync(latestRelease, architecture, cancellationToken).ConfigureAwait(false);
    }

    private void EnsureProgramDataRootAndCache()
    {
        programDataDirectoryProvisioningService.EnsureRoot();
        programDataDirectoryProvisioningService.EnsureKnownDirectory(
            ProgramDataPolicies.WindowsTerminalCache);
        programDataDirectoryProvisioningService.EnsureKnownDirectory(
            ProgramDataPolicies.WindowsTerminalDeploymentWork);
    }

    private async Task DeployCachedZipAsync(string cachedZipPath, Version expectedVersion, CancellationToken cancellationToken)
    {
        var operation = CreateDeploymentOperation(cachedZipPath, expectedVersion);
        try
        {
            await deploymentConsoleRunner.RunAsync(operation, cancellationToken).ConfigureAwait(false);
            EnsurePrivilegeSpecificExecutables(operation.StagingRootPath, overwriteExisting: true);
            ValidateStagedDeployment(operation);
            ReplaceSharedDeploymentFromStaging(operation);
        }
        finally
        {
            deploymentDirectoryCleaner.TryDeleteIfExists(operation.OperationWorkRootPath);
        }
    }

    private WindowsTerminalDeploymentOperation CreateDeploymentOperation(string cachedZipPath, Version expectedVersion)
    {
        var operationId = Guid.NewGuid().ToString("N");
        var operation = new WindowsTerminalDeploymentOperation(
            cachedZipPath,
            deploymentPaths.SharedRootPath,
            deploymentPaths.GetOperationWorkRootPath(operationId),
            deploymentPaths.GetStagingRootPath(operationId),
            deploymentPaths.GetExtractRootPath(operationId),
            deploymentPaths.GetBackupRootPath(operationId),
            expectedVersion,
            WindowsTerminalDeploymentPaths.DeploymentVersionFileName);

        EnsureOperationPathUnderRoot(operation.OperationWorkRootPath);
        EnsureOperationPathUnderRoot(operation.StagingRootPath);
        EnsureOperationPathUnderRoot(operation.ExtractRootPath);
        EnsureOperationPathUnderRoot(operation.BackupRootPath);

        Directory.CreateDirectory(operation.StagingRootPath);
        Directory.CreateDirectory(operation.ExtractRootPath);

        if (PathExists(operation.BackupRootPath))
            throw new InvalidOperationException("Windows Terminal deployment backup path already exists.");

        if (Directory.EnumerateFileSystemEntries(operation.StagingRootPath).Any() ||
            Directory.EnumerateFileSystemEntries(operation.ExtractRootPath).Any())
        {
            throw new InvalidOperationException("Windows Terminal deployment work directories must be empty.");
        }

        return operation;
    }

    private void EnsureOperationPathUnderRoot(string path)
    {
        if (!programDataPathPolicyService.IsUnderRoot(path))
            throw new InvalidOperationException($"Windows Terminal deployment path '{path}' is outside managed ProgramData.");
    }

    private void ValidateStagedDeployment(WindowsTerminalDeploymentOperation operation)
    {
        if (!File.Exists(Path.Combine(operation.StagingRootPath, "WindowsTerminal.exe")))
            throw new InvalidOperationException("Windows Terminal staged deployment is missing WindowsTerminal.exe.");

        foreach (var executablePath in deploymentPaths.GetSharedExecutablePaths())
        {
            var stagedExecutablePath = Path.Combine(
                operation.StagingRootPath,
                Path.GetFileName(executablePath));
            if (!File.Exists(stagedExecutablePath))
                throw new InvalidOperationException($"Windows Terminal staged deployment is missing {Path.GetFileName(stagedExecutablePath)}.");
        }

        var deploymentVersionPath = Path.Combine(operation.StagingRootPath, operation.DeploymentVersionFileName);
        if (!File.Exists(deploymentVersionPath) ||
            !Version.TryParse(File.ReadAllText(deploymentVersionPath).Trim(), out var deployedVersion) ||
            deployedVersion != operation.ExpectedVersion)
        {
            throw new InvalidOperationException(
                $"Windows Terminal staged deployment did not produce version {operation.ExpectedVersion}.");
        }
    }

    private bool EnsurePrivilegeSpecificExecutables(string deploymentRootPath, bool overwriteExisting)
    {
        var sourceExecutablePath = Path.Combine(deploymentRootPath, WindowsTerminalDeploymentPaths.SharedExecutableFileName);
        if (!File.Exists(sourceExecutablePath))
            throw new InvalidOperationException("Windows Terminal staged deployment is missing WindowsTerminal.exe.");

        var copiedAny = false;
        foreach (var executablePath in deploymentPaths.GetSharedExecutablePaths().Skip(1))
        {
            var targetPath = Path.Combine(deploymentRootPath, Path.GetFileName(executablePath));
            if (!overwriteExisting && File.Exists(targetPath))
                continue;

            File.Copy(sourceExecutablePath, targetPath, overwrite: overwriteExisting);
            copiedAny = true;
        }

        return copiedAny;
    }

    private void ReplaceSharedDeploymentFromStaging(WindowsTerminalDeploymentOperation operation)
    {
        if (!Directory.Exists(operation.StagingRootPath))
            throw new InvalidOperationException("Windows Terminal staging directory is missing.");

        if (PathExists(operation.BackupRootPath))
            throw new InvalidOperationException("Windows Terminal deployment backup path already exists.");

        var movedCurrent = false;
        var movedStaging = false;
        try
        {
            if (Directory.Exists(operation.SharedRootPath))
            {
                Directory.Move(operation.SharedRootPath, operation.BackupRootPath);
                movedCurrent = true;
            }

            Directory.Move(operation.StagingRootPath, operation.SharedRootPath);
            movedStaging = true;
            deploymentDirectoryCleaner.TryDeleteIfExists(operation.BackupRootPath);
        }
        catch (Exception)
        {
            if (movedStaging)
                deploymentDirectoryCleaner.TryDeleteIfExists(operation.SharedRootPath);

            if (movedCurrent && Directory.Exists(operation.BackupRootPath))
            {
                Directory.Move(operation.BackupRootPath, operation.SharedRootPath);
            }

            throw;
        }
    }

    private bool IsSharedDeploymentVersion(Version expectedVersion)
        => File.Exists(deploymentPaths.SharedExecutablePath) &&
           TryReadSharedDeploymentVersion(out var deployedVersion) &&
           deployedVersion == expectedVersion;

    private bool TryReadSharedDeploymentVersion(out Version deployedVersion)
        => Version.TryParse(ReadDeploymentVersionText(), out deployedVersion!);

    private string? ReadDeploymentVersionText()
        => File.Exists(deploymentPaths.SharedDeploymentVersionPath)
            ? File.ReadAllText(deploymentPaths.SharedDeploymentVersionPath).Trim()
            : null;

    private void RecoverInterruptedDeployment()
    {
        if (!Directory.Exists(deploymentPaths.DeploymentWorkRootPath))
            return;

        var sharedRootExists = Directory.Exists(deploymentPaths.SharedRootPath);
        var operationDirectories = Directory.GetDirectories(deploymentPaths.DeploymentWorkRootPath);

        if (!sharedRootExists)
        {
            var recoverySource = operationDirectories
                .OrderByDescending(Directory.GetLastWriteTimeUtc)
                .Select(TryGetRecoverableBackupPath)
                .FirstOrDefault(path => path != null);
            if (recoverySource != null)
            {
                Directory.Move(recoverySource, deploymentPaths.SharedRootPath);
            }
        }

        foreach (var directoryPath in operationDirectories)
            deploymentDirectoryCleaner.TryDeleteIfExists(directoryPath);
    }

    private string? TryGetRecoverableBackupPath(string operationDirectoryPath)
    {
        try
        {
            if (!Directory.Exists(operationDirectoryPath))
            {
                return null;
            }
        }
        catch (Exception)
        {
            return null;
        }

        var backupPath = Path.Combine(operationDirectoryPath, "backup");
        if (!Directory.Exists(backupPath) ||
            !File.Exists(Path.Combine(backupPath, "WindowsTerminal.exe")))
        {
            return null;
        }

        try
        {
            programDataDirectoryProvisioningService.EnsureDirectoryTreeInheritsFromRoot(
                backupPath,
                ProgramDataDirectoryAclProfile.SharedExecutableReadExecute);
        }
        catch (Exception)
        {
            return null;
        }

        return backupPath;
    }

    private bool PathExists(string path)
        => Directory.Exists(path) || File.Exists(path);

    private bool IsDeploymentLocked()
    {
        foreach (var filePath in Directory.EnumerateFiles(deploymentPaths.SharedRootPath, "*", SearchOption.AllDirectories))
        {
            try
            {
                using var _ = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
            }
            catch (IOException)
            {
                return true;
            }
        }

        return false;
    }
}
