using RunFence.Account.UI;
using RunFence.Acl;
using Xunit;

namespace RunFence.Tests;

public sealed class WindowsTerminalDeploymentServiceTests : IDisposable
{
    private readonly TempDirectory _tempDirectory = new("RunFence_WindowsTerminalDeployment");

    [Fact]
    public async Task EnsureSharedDeploymentReady_WhenSharedDeploymentIsLocked_SkipsGitHubAndDeployment()
    {
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.SharedRootPath);
        File.WriteAllText(paths.SharedExecutablePath, string.Empty);

        var securityService = new TestProgramDataDirectorySecurityService(_tempDirectory.Path);
        var releaseClient = new FakeWindowsTerminalReleaseClient(new Version(1, 24, 11321, 0));
        var runner = new FakeWindowsTerminalDeploymentConsoleRunner();
        var service = CreateService(paths, securityService, releaseClient, runner);

        using var lockHandle = File.Open(paths.SharedExecutablePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await service.EnsureSharedDeploymentReadyAsync(CancellationToken.None);

        Assert.Equal(0, releaseClient.CallCount);
        Assert.Equal(0, runner.CallCount);
    }

    [Fact]
    public async Task EnsureSharedDeploymentReady_WhenCachedZipMatchesLatest_UsesCacheAndDeploysWithoutRedownload()
    {
        var latestVersion = new Version(1, 24, 11321, 0);
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.DownloadCacheDirectoryPath);
        File.WriteAllText(paths.GetCachedZipPath(latestVersion, WindowsTerminalReleaseClient.GetArchitectureSuffix()), "zip");
        Directory.CreateDirectory(paths.SharedRootPath);
        File.WriteAllText(paths.SharedExecutablePath, string.Empty);

        var securityService = new TestProgramDataDirectorySecurityService(_tempDirectory.Path);
        var releaseClient = new FakeWindowsTerminalReleaseClient(latestVersion);
        var runner = new FakeWindowsTerminalDeploymentConsoleRunner();
        var service = CreateService(paths, securityService, releaseClient, runner);

        await service.EnsureSharedDeploymentReadyAsync(CancellationToken.None);

        Assert.Equal(1, releaseClient.CallCount);
        Assert.Equal(1, runner.CallCount);
        Assert.Equal(0, runner.DownloadCallCount);
        Assert.True(File.Exists(paths.SharedHelperCommandPath));
        Assert.Equal(latestVersion.ToString(), File.ReadAllText(paths.SharedDeploymentVersionPath).Trim());
        AssertSharedExecutableVariantsExist(paths);
        Assert.Contains(paths.SharedRootPath, securityService.HardenedDirectories);
        Assert.Contains(paths.SharedRootPath, securityService.InheritedTrees);
        Assert.NotNull(runner.LastOperation);
        Assert.StartsWith(
            paths.DeploymentWorkRootPath + Path.DirectorySeparatorChar,
            runner.LastOperation.Value.OperationWorkRootPath,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(runner.LastOperation.Value.OperationWorkRootPath, securityService.HardenedDirectories);
        Assert.DoesNotContain(runner.LastOperation.Value.ExtractRootPath, securityService.HardenedDirectories);
        Assert.False(Directory.Exists(runner.LastOperation.Value.OperationWorkRootPath));
    }

    [Fact]
    public async Task TryDeployLatestCachedZipIfNewerThanShared_WhenCachedZipIsNewer_DeploysFromCacheWithoutGitHub()
    {
        var deployedVersion = new Version(1, 24, 10000, 0);
        var cachedVersion = new Version(1, 24, 11321, 0);
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.DownloadCacheDirectoryPath);
        File.WriteAllText(paths.GetCachedZipPath(cachedVersion, WindowsTerminalReleaseClient.GetArchitectureSuffix()), "zip");
        Directory.CreateDirectory(paths.SharedRootPath);
        File.WriteAllText(paths.SharedExecutablePath, string.Empty);
        WriteDeploymentVersion(paths.SharedDeploymentVersionPath, deployedVersion);

        var securityService = new TestProgramDataDirectorySecurityService(_tempDirectory.Path);
        var releaseClient = new FakeWindowsTerminalReleaseClient(cachedVersion);
        var runner = new FakeWindowsTerminalDeploymentConsoleRunner();
        var service = CreateService(paths, securityService, releaseClient, runner);

        var deployed = await service.TryDeployLatestCachedZipIfNewerThanSharedAsync(CancellationToken.None);

        Assert.True(deployed);
        Assert.Equal(0, releaseClient.CallCount);
        Assert.Equal(1, runner.CallCount);
        Assert.Equal(0, runner.DownloadCallCount);
        Assert.Equal(cachedVersion.ToString(), File.ReadAllText(paths.SharedDeploymentVersionPath).Trim());
        Assert.True(File.Exists(paths.SharedHelperCommandPath));
        AssertSharedExecutableVariantsExist(paths);
        Assert.Contains(paths.SharedRootPath, securityService.InheritedTrees);
    }

    [Fact]
    public async Task TryDeployLatestCachedZipIfNewerThanShared_WhenCachedZipIsNotNewer_DoesNotDeploy()
    {
        var version = new Version(1, 24, 11321, 0);
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.DownloadCacheDirectoryPath);
        File.WriteAllText(paths.GetCachedZipPath(version, WindowsTerminalReleaseClient.GetArchitectureSuffix()), "zip");
        Directory.CreateDirectory(paths.SharedRootPath);
        File.WriteAllText(paths.SharedExecutablePath, string.Empty);
        WriteDeploymentVersion(paths.SharedDeploymentVersionPath, version);

        var securityService = new TestProgramDataDirectorySecurityService(_tempDirectory.Path);
        var releaseClient = new FakeWindowsTerminalReleaseClient(version);
        var runner = new FakeWindowsTerminalDeploymentConsoleRunner();
        var service = CreateService(paths, securityService, releaseClient, runner);

        var deployed = await service.TryDeployLatestCachedZipIfNewerThanSharedAsync(CancellationToken.None);

        Assert.False(deployed);
        Assert.Equal(0, releaseClient.CallCount);
        Assert.Equal(0, runner.CallCount);
    }

    [Fact]
    public async Task TryDeployLatestCachedZipIfNewerThanShared_WhenSharedDeploymentIsLocked_DoesNotDeploy()
    {
        var deployedVersion = new Version(1, 24, 10000, 0);
        var cachedVersion = new Version(1, 24, 11321, 0);
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.DownloadCacheDirectoryPath);
        File.WriteAllText(paths.GetCachedZipPath(cachedVersion, WindowsTerminalReleaseClient.GetArchitectureSuffix()), "zip");
        Directory.CreateDirectory(paths.SharedRootPath);
        File.WriteAllText(paths.SharedExecutablePath, string.Empty);
        WriteDeploymentVersion(paths.SharedDeploymentVersionPath, deployedVersion);

        var securityService = new TestProgramDataDirectorySecurityService(_tempDirectory.Path);
        var releaseClient = new FakeWindowsTerminalReleaseClient(cachedVersion);
        var runner = new FakeWindowsTerminalDeploymentConsoleRunner();
        var service = CreateService(paths, securityService, releaseClient, runner);

        using var lockHandle = File.Open(paths.SharedExecutablePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var deployed = await service.TryDeployLatestCachedZipIfNewerThanSharedAsync(CancellationToken.None);

        Assert.False(deployed);
        Assert.Equal(0, releaseClient.CallCount);
        Assert.Equal(0, runner.CallCount);
    }

    [Fact]
    public async Task TryDeployLatestCachedZipIfNewerThanShared_WhenDeploymentAlreadyRunning_WaitsForDeployment()
    {
        var deployedVersion = new Version(1, 24, 10000, 0);
        var releaseVersion = new Version(1, 24, 11321, 0);
        var cachedVersion = new Version(1, 24, 11400, 0);
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.DownloadCacheDirectoryPath);
        File.WriteAllText(paths.GetCachedZipPath(cachedVersion, WindowsTerminalReleaseClient.GetArchitectureSuffix()), "zip");
        Directory.CreateDirectory(paths.SharedRootPath);
        File.WriteAllText(paths.SharedExecutablePath, string.Empty);
        WriteDeploymentVersion(paths.SharedDeploymentVersionPath, deployedVersion);

        var runStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowRun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var securityService = new TestProgramDataDirectorySecurityService(_tempDirectory.Path);
        var runner = new FakeWindowsTerminalDeploymentConsoleRunner(runStarted: runStarted, allowRun: allowRun);
        var service = CreateService(
            paths,
            securityService,
            new FakeWindowsTerminalReleaseClient(releaseVersion),
            runner);

        var sharedDeploymentTask = service.EnsureSharedDeploymentReadyAsync(CancellationToken.None);
        await runStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var cachedDeploymentTask = service.TryDeployLatestCachedZipIfNewerThanSharedAsync(CancellationToken.None);
        await Task.Yield();

        Assert.False(cachedDeploymentTask.IsCompleted);
        allowRun.SetResult();
        await sharedDeploymentTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(await cachedDeploymentTask.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, runner.CallCount);
    }

    [Fact]
    public async Task EnsureSharedDeploymentReady_WhenSharedDeploymentMissingAndCacheIsLatest_DeploysFromCache()
    {
        var latestVersion = new Version(1, 24, 11321, 0);
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.DownloadCacheDirectoryPath);
        File.WriteAllText(paths.GetCachedZipPath(latestVersion, WindowsTerminalReleaseClient.GetArchitectureSuffix()), "zip");

        var securityService = new TestProgramDataDirectorySecurityService(_tempDirectory.Path);
        var releaseClient = new FakeWindowsTerminalReleaseClient(latestVersion);
        var runner = new FakeWindowsTerminalDeploymentConsoleRunner();
        var service = CreateService(paths, securityService, releaseClient, runner);

        await service.EnsureSharedDeploymentReadyAsync(CancellationToken.None);

        Assert.Equal(1, runner.CallCount);
        Assert.Equal(0, runner.DownloadCallCount);
        Assert.True(File.Exists(paths.SharedExecutablePath));
        AssertSharedExecutableVariantsExist(paths);
        Assert.True(File.Exists(paths.SharedHelperCommandPath));
        Assert.Contains(paths.SharedRootPath, securityService.HardenedDirectories);
        Assert.Contains(paths.SharedRootPath, securityService.InheritedTrees);
    }

    [Fact]
    public async Task EnsureSharedDeploymentReady_WhenHelperRepairChangesExistingFile_ReappliesSharedTreeSecurity()
    {
        var latestVersion = new Version(1, 24, 11321, 0);
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.SharedRootPath);
        File.WriteAllText(paths.SharedExecutablePath, string.Empty);
        WriteDeploymentVersion(paths.SharedDeploymentVersionPath, latestVersion);
        Directory.CreateDirectory(paths.SharedHelperPathDirectory);
        File.WriteAllText(paths.SharedHelperCommandPath, "@echo off\r\n\"%~dp0..\\WindowsTerminal.exe\" %*\r\n");

        var securityService = new TestProgramDataDirectorySecurityService(_tempDirectory.Path)
        {
            ManagedFileOwnerResult = true
        };
        var service = CreateService(
            paths,
            securityService,
            new FakeWindowsTerminalReleaseClient(latestVersion),
            new FakeWindowsTerminalDeploymentConsoleRunner());

        await service.EnsureSharedDeploymentReadyAsync(CancellationToken.None);

        Assert.Equal(
            2,
            securityService.InheritedTrees.Count(path => string.Equals(path, paths.SharedRootPath, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task EnsureSharedDeploymentReady_WhenDeploymentAlreadyMatchesLatestAndCacheIsMissing_DoesNotRedeploy()
    {
        var latestVersion = new Version(1, 24, 11321, 0);
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.SharedRootPath);
        File.WriteAllText(paths.SharedExecutablePath, string.Empty);
        WriteDeploymentVersion(paths.SharedDeploymentVersionPath, latestVersion);

        var securityService = new TestProgramDataDirectorySecurityService(_tempDirectory.Path);
        var releaseClient = new FakeWindowsTerminalReleaseClient(latestVersion);
        var runner = new FakeWindowsTerminalDeploymentConsoleRunner();
        var service = CreateService(paths, securityService, releaseClient, runner);

        await service.EnsureSharedDeploymentReadyAsync(CancellationToken.None);

        Assert.Equal(1, releaseClient.CallCount);
        Assert.Equal(0, runner.CallCount);
        Assert.True(File.Exists(paths.SharedHelperCommandPath));
        AssertSharedExecutableVariantsExist(paths);
        Assert.Contains(paths.SharedRootPath, securityService.HardenedDirectories);
        Assert.Contains(paths.SharedRootPath, securityService.InheritedTrees);
    }

    [Fact]
    public async Task EnsureSharedDeploymentReady_WhenPreviousSwapLeftBackupOnly_RestoresBackupBeforeContinuing()
    {
        var latestVersion = new Version(1, 24, 11321, 0);
        var paths = CreatePaths();
        var backupPath = paths.GetBackupRootPath("orphaned");
        Directory.CreateDirectory(paths.DownloadCacheDirectoryPath);
        File.WriteAllText(paths.GetCachedZipPath(latestVersion, WindowsTerminalReleaseClient.GetArchitectureSuffix()), "zip");
        Directory.CreateDirectory(backupPath);
        File.WriteAllText(Path.Combine(backupPath, "WindowsTerminal.exe"), string.Empty);
        WriteDeploymentVersion(Path.Combine(backupPath, WindowsTerminalDeploymentPaths.DeploymentVersionFileName), latestVersion);

        var securityService = new TestProgramDataDirectorySecurityService(_tempDirectory.Path);
        var releaseClient = new FakeWindowsTerminalReleaseClient(latestVersion);
        var runner = new FakeWindowsTerminalDeploymentConsoleRunner();
        var service = CreateService(paths, securityService, releaseClient, runner);

        await service.EnsureSharedDeploymentReadyAsync(CancellationToken.None);

        Assert.True(File.Exists(paths.SharedExecutablePath));
        AssertSharedExecutableVariantsExist(paths);
        Assert.False(Directory.Exists(backupPath));
        Assert.Equal(0, runner.CallCount);
    }

    [Fact]
    public async Task EnsureSharedDeploymentReady_WhenOnlyStagingDirectoryRemains_DoesNotPromoteCanceledStagingPayload()
    {
        var latestVersion = new Version(1, 24, 11321, 0);
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.DownloadCacheDirectoryPath);
        File.WriteAllText(paths.GetCachedZipPath(latestVersion, WindowsTerminalReleaseClient.GetArchitectureSuffix()), "zip");
        var stagingPath = paths.GetStagingRootPath("canceled");
        Directory.CreateDirectory(stagingPath);
        File.WriteAllText(Path.Combine(stagingPath, "WindowsTerminal.exe"), string.Empty);

        var securityService = new TestProgramDataDirectorySecurityService(_tempDirectory.Path);
        var releaseClient = new FakeWindowsTerminalReleaseClient(latestVersion);
        var runner = new FakeWindowsTerminalDeploymentConsoleRunner();
        var service = CreateService(paths, securityService, releaseClient, runner);

        await service.EnsureSharedDeploymentReadyAsync(CancellationToken.None);

        Assert.True(File.Exists(paths.SharedExecutablePath));
        AssertSharedExecutableVariantsExist(paths);
        Assert.False(Directory.Exists(stagingPath));
        Assert.Equal(1, runner.CallCount);
    }

    [Fact]
    public async Task EnsureSharedDeploymentReady_WhenStaleOperationRootIsJunction_DeletesJunctionWithoutDescendingIntoTarget()
    {
        var latestVersion = new Version(1, 24, 11321, 0);
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.SharedRootPath);
        File.WriteAllText(paths.SharedExecutablePath, string.Empty);
        WriteDeploymentVersion(paths.SharedDeploymentVersionPath, latestVersion);
        Directory.CreateDirectory(paths.DeploymentWorkRootPath);
        var targetPath = Path.Combine(_tempDirectory.Path, "outside-stale-target");
        Directory.CreateDirectory(targetPath);
        var targetFilePath = Path.Combine(targetPath, "sentinel.txt");
        File.WriteAllText(targetFilePath, "keep");
        var staleOperationPath = paths.GetOperationWorkRootPath("stale-junction");
        JunctionHelper.CreateJunction(staleOperationPath, targetPath);

        var runner = new FakeWindowsTerminalDeploymentConsoleRunner();
        var securityService = new TestProgramDataDirectorySecurityService(_tempDirectory.Path);
        var service = CreateService(
            paths,
            securityService,
            new FakeWindowsTerminalReleaseClient(latestVersion),
            runner);

        await service.EnsureSharedDeploymentReadyAsync(CancellationToken.None);

        Assert.False(Directory.Exists(staleOperationPath));
        Assert.True(Directory.Exists(targetPath));
        Assert.True(File.Exists(targetFilePath));
        Assert.Equal(0, runner.CallCount);
    }

    [Fact]
    public async Task EnsureSharedDeploymentReady_WhenSharedMissingAndStaleOperationRootIsJunction_DeploysWithoutDescendingIntoTarget()
    {
        var latestVersion = new Version(1, 24, 11321, 0);
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.DownloadCacheDirectoryPath);
        File.WriteAllText(paths.GetCachedZipPath(latestVersion, WindowsTerminalReleaseClient.GetArchitectureSuffix()), "zip");
        Directory.CreateDirectory(paths.DeploymentWorkRootPath);
        var targetPath = Path.Combine(_tempDirectory.Path, "outside-stale-target");
        Directory.CreateDirectory(targetPath);
        var targetFilePath = Path.Combine(targetPath, "sentinel.txt");
        File.WriteAllText(targetFilePath, "keep");
        var staleOperationPath = paths.GetOperationWorkRootPath("stale-junction");
        JunctionHelper.CreateJunction(staleOperationPath, targetPath);

        var runner = new FakeWindowsTerminalDeploymentConsoleRunner();
        var securityService = new TestProgramDataDirectorySecurityService(_tempDirectory.Path);
        var service = CreateService(
            paths,
            securityService,
            new FakeWindowsTerminalReleaseClient(latestVersion),
            runner);

        await service.EnsureSharedDeploymentReadyAsync(CancellationToken.None);

        Assert.True(File.Exists(paths.SharedExecutablePath));
        AssertSharedExecutableVariantsExist(paths);
        Assert.False(Directory.Exists(staleOperationPath));
        Assert.True(Directory.Exists(targetPath));
        Assert.True(File.Exists(targetFilePath));
        Assert.Equal(1, runner.CallCount);
    }

    [Fact]
    public async Task EnsureSharedDeploymentReady_WhenSharedMissingAndStaleBackupIsJunction_DeploysWithoutPromotingTarget()
    {
        var latestVersion = new Version(1, 24, 11321, 0);
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.DownloadCacheDirectoryPath);
        File.WriteAllText(paths.GetCachedZipPath(latestVersion, WindowsTerminalReleaseClient.GetArchitectureSuffix()), "zip");
        var operationRootPath = paths.GetOperationWorkRootPath("stale-operation");
        Directory.CreateDirectory(operationRootPath);
        var targetPath = Path.Combine(_tempDirectory.Path, "outside-backup-target");
        Directory.CreateDirectory(targetPath);
        var targetExePath = Path.Combine(targetPath, "WindowsTerminal.exe");
        File.WriteAllText(targetExePath, string.Empty);
        var backupPath = Path.Combine(operationRootPath, "backup");
        JunctionHelper.CreateJunction(backupPath, targetPath);

        var runner = new FakeWindowsTerminalDeploymentConsoleRunner();
        var securityService = new TestProgramDataDirectorySecurityService(_tempDirectory.Path);
        var service = CreateService(
            paths,
            securityService,
            new FakeWindowsTerminalReleaseClient(latestVersion),
            runner);

        await service.EnsureSharedDeploymentReadyAsync(CancellationToken.None);

        Assert.True(File.Exists(paths.SharedExecutablePath));
        AssertSharedExecutableVariantsExist(paths);
        Assert.False(Directory.Exists(operationRootPath));
        Assert.True(Directory.Exists(targetPath));
        Assert.True(File.Exists(targetExePath));
        Assert.Equal(1, runner.CallCount);
    }

    [Fact]
    public async Task EnsureSharedDeploymentReady_WhenDeploymentRunnerDoesNotStampExpectedVersion_Throws()
    {
        var latestVersion = new Version(1, 24, 11321, 0);
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.DownloadCacheDirectoryPath);
        File.WriteAllText(paths.GetCachedZipPath(latestVersion, WindowsTerminalReleaseClient.GetArchitectureSuffix()), "zip");

        var securityService = new TestProgramDataDirectorySecurityService(_tempDirectory.Path);
        var service = CreateService(
            paths,
            securityService,
            new FakeWindowsTerminalReleaseClient(latestVersion),
            new FakeWindowsTerminalDeploymentConsoleRunner(writeDeploymentVersion: false));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.EnsureSharedDeploymentReadyAsync(CancellationToken.None));

        Assert.Contains($"did not produce version {latestVersion}", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnsureSharedDeploymentReady_WhenDeploymentWorkRootIsRejected_DoesNotRunDeployment()
    {
        var latestVersion = new Version(1, 24, 11321, 0);
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.DownloadCacheDirectoryPath);
        File.WriteAllText(paths.GetCachedZipPath(latestVersion, WindowsTerminalReleaseClient.GetArchitectureSuffix()), "zip");

        var securityService = new TestProgramDataDirectorySecurityService(_tempDirectory.Path);
        securityService.RejectDirectoryName(ProgramDataPolicies.WindowsTerminalDeploymentWork.RelativePath);
        var runner = new FakeWindowsTerminalDeploymentConsoleRunner();
        var service = CreateService(
            paths,
            securityService,
            new FakeWindowsTerminalReleaseClient(latestVersion),
            runner);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.EnsureSharedDeploymentReadyAsync(CancellationToken.None));

        Assert.Contains(ProgramDataPolicies.WindowsTerminalDeploymentWork.RelativePath, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, runner.CallCount);
    }

    public void Dispose()
    {
        _tempDirectory.Dispose();
    }

    private WindowsTerminalDeploymentPaths CreatePaths()
        => new(new TestProgramDataKnownPathResolver(_tempDirectory.Path));

    private WindowsTerminalDeploymentService CreateService(
        WindowsTerminalDeploymentPaths paths,
        TestProgramDataDirectorySecurityService securityService,
        IWindowsTerminalReleaseClient releaseClient,
        IWindowsTerminalDeploymentConsoleRunner runner)
    {
        var sharedDeploymentSecurityService = new WindowsTerminalSharedDeploymentSecurityService(
            securityService,
            securityService,
            paths);
        var packageCacheService = new WindowsTerminalPackageCacheService(
            paths,
            runner,
            new PassThroughWindowsTerminalPackageVerifier());
        return new WindowsTerminalDeploymentService(
            securityService,
            securityService,
            paths,
            releaseClient,
            packageCacheService,
            sharedDeploymentSecurityService,
            runner,
            new WindowsTerminalDeploymentDirectoryCleaner());
    }

    private static void WriteDeploymentVersion(string path, Version version)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, version.ToString());
    }

    private static void AssertSharedExecutableVariantsExist(WindowsTerminalDeploymentPaths paths)
    {
        foreach (var executablePath in paths.GetSharedExecutablePaths())
            Assert.True(File.Exists(executablePath), $"Expected managed Windows Terminal executable '{executablePath}'.");
    }

    private sealed class TestProgramDataDirectorySecurityService(string rootPath)
        : IProgramDataDirectoryProvisioningService,
            IProgramDataPathPolicyService,
            IProgramDataManagedObjectRepairService
    {
        public List<string> HardenedDirectories { get; } = [];
        public List<string> HardenedFiles { get; } = [];
        public List<string> InheritedTrees { get; } = [];
        public Dictionary<string, List<ProgramDataDirectoryAclProfile>> DirectoryProfileHistory { get; } =
            new(StringComparer.OrdinalIgnoreCase);
        public ProgramDataSecurityRepairResult ManagedFileSecurityResult { get; init; }
        public bool ManagedFileOwnerResult { get; init; }
        private readonly HashSet<string> rejectedDirectoryNames = new(StringComparer.OrdinalIgnoreCase);

        public void RejectDirectoryName(string directoryName) => rejectedDirectoryNames.Add(directoryName);

        public string EnsureRoot()
        {
            Directory.CreateDirectory(rootPath);
            return rootPath;
        }

        public string EnsureSubdirectory(string relativePath, ProgramDataDirectoryAclProfile aclProfile)
        {
            var absolutePath = Path.Combine(rootPath, relativePath);
            if (rejectedDirectoryNames.Contains(Path.GetFileName(absolutePath)))
                throw new InvalidOperationException($"Rejected managed directory '{absolutePath}'.");

            Directory.CreateDirectory(absolutePath);
            return absolutePath;
        }

        public string EnsureKnownDirectory(ProgramDataDirectoryPolicy policy)
            => EnsureSubdirectory(policy.RelativePath, policy.Profile);

        public void EnsureKnownDirectoryTreeInheritsFromRoot(ProgramDataDirectoryPolicy policy)
            => EnsureDirectoryTreeInheritsFromRoot(Path.Combine(rootPath, policy.RelativePath), policy.Profile);

        public void EnsureDirectoryUnderRoot(string directoryPath, ProgramDataDirectoryAclProfile aclProfile)
        {
            if (rejectedDirectoryNames.Contains(Path.GetFileName(directoryPath)))
                throw new InvalidOperationException($"Rejected managed directory '{directoryPath}'.");

            Directory.CreateDirectory(directoryPath);
            HardenedDirectories.Add(directoryPath);
            if (!DirectoryProfileHistory.TryGetValue(directoryPath, out var profiles))
            {
                profiles = [];
                DirectoryProfileHistory[directoryPath] = profiles;
            }

            profiles.Add(aclProfile);
        }

        public void EnsureDirectoryTreeInheritsFromRoot(
            string directoryPath,
            ProgramDataDirectoryAclProfile rootAclProfile)
        {
            EnsureDirectoryUnderRoot(directoryPath, rootAclProfile);
            InheritedTrees.Add(directoryPath);
        }

        public bool EnsureManagedFileOwner(string filePath)
        {
            return ManagedFileOwnerResult;
        }

        public bool EnsureManagedDirectoryOwner(string directoryPath)
        {
            return false;
        }

        public ProgramDataSecurityRepairResult EnsureManagedFileSecurity(string filePath, ProgramDataFileAclProfile aclProfile)
        {
            HardenedFiles.Add(filePath);
            return ManagedFileSecurityResult;
        }

        public void EnsureTraverseOnlyAccess(string directoryPath, string sid, ProgramDataDirectoryAclProfile aclProfile)
        {
        }

        public bool EnsureManagedFileOwner(string filePath, IReadOnlyCollection<string> expectedAdditionalOwnerSids)
        {
            return ManagedFileOwnerResult;
        }

        public bool EnsureManagedDirectoryOwner(string directoryPath, IReadOnlyCollection<string> expectedAdditionalOwnerSids)
        {
            return false;
        }

        public bool IsUnderRoot(string path) => true;
    }

    private sealed class FakeWindowsTerminalReleaseClient(Version version) : IWindowsTerminalReleaseClient
    {
        public int CallCount { get; private set; }

        public Task<WindowsTerminalReleaseInfo> GetLatestReleaseAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            var arch = WindowsTerminalReleaseClient.GetArchitectureSuffix();
            return Task.FromResult(new WindowsTerminalReleaseInfo(
                version,
                $"Microsoft.WindowsTerminal_{version}_{arch}.zip",
                $"https://example.invalid/Microsoft.WindowsTerminal_{version}_{arch}.zip"));
        }
    }

    private sealed class PassThroughWindowsTerminalPackageVerifier : IWindowsTerminalPackageVerifier
    {
        public void VerifyPackage(string zipPath)
        {
        }
    }

    private sealed class FakeWindowsTerminalDeploymentConsoleRunner(
        bool writeDeploymentVersion = true,
        TaskCompletionSource? runStarted = null,
        TaskCompletionSource? allowRun = null)
        : IWindowsTerminalDeploymentConsoleRunner
    {
        public int CallCount { get; private set; }
        public int DownloadCallCount { get; private set; }
        public string? LastDownloadUrl { get; private set; }
        public string? LastDownloadDestinationPath { get; private set; }
        public WindowsTerminalDeploymentOperation? LastOperation { get; private set; }

        public Task DownloadAsync(WindowsTerminalPackageDownloadOperation operation, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DownloadCallCount++;
            LastDownloadUrl = operation.DownloadUrl;
            LastDownloadDestinationPath = operation.DestinationPath;
            Directory.CreateDirectory(Path.GetDirectoryName(operation.DestinationPath)!);
            File.WriteAllText(operation.DestinationPath, "zip");
            return Task.CompletedTask;
        }

        public async Task RunAsync(WindowsTerminalDeploymentOperation operation, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            LastOperation = operation;
            runStarted?.TrySetResult();
            if (allowRun != null)
                await allowRun.Task.WaitAsync(cancellationToken);

            WriteBaseDeploymentPayload(operation.StagingRootPath);
            var arch = WindowsTerminalReleaseClient.GetArchitectureSuffix();
            Assert.True(WindowsTerminalDeploymentPaths.TryParseCachedZipVersion(operation.CachedZipPath, arch, out var version));
            Assert.Equal(operation.ExpectedVersion, version);
            if (writeDeploymentVersion)
                WriteDeploymentVersion(
                    Path.Combine(operation.StagingRootPath, operation.DeploymentVersionFileName),
                    operation.ExpectedVersion);
            AssertPrivilegeSpecificExecutablesDoNotExist(operation.StagingRootPath);
        }

        private static void WriteBaseDeploymentPayload(string stagingRootPath)
        {
            File.WriteAllText(Path.Combine(stagingRootPath, WindowsTerminalDeploymentPaths.SharedExecutableFileName), string.Empty);
        }

        private static void AssertPrivilegeSpecificExecutablesDoNotExist(string stagingRootPath)
        {
            Assert.False(File.Exists(Path.Combine(stagingRootPath, WindowsTerminalDeploymentPaths.ElevatedExecutableFileName)));
            Assert.False(File.Exists(Path.Combine(stagingRootPath, WindowsTerminalDeploymentPaths.HighIntegrityExecutableFileName)));
            Assert.False(File.Exists(Path.Combine(stagingRootPath, WindowsTerminalDeploymentPaths.IsolatedExecutableFileName)));
            Assert.False(File.Exists(Path.Combine(stagingRootPath, WindowsTerminalDeploymentPaths.LowIntegrityExecutableFileName)));
        }
    }
}
