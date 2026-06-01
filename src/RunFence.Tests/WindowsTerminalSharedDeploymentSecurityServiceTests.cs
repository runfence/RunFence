using RunFence.Account.UI;
using RunFence.Acl;
using Xunit;

namespace RunFence.Tests;

public sealed class WindowsTerminalSharedDeploymentSecurityServiceTests : IDisposable
{
    private readonly TempDirectory _tempDirectory = new("RunFence_WindowsTerminalSharedDeploymentSecurity");

    [Fact]
    public void EnsureHelperFiles_WhenHelperIsMissing_CreatesManagedHelperFile()
    {
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.SharedRootPath);
        var programDataService = new FakeProgramDataSecurityService(_tempDirectory.Path);
        var service = CreateService(paths, programDataService);

        var changed = service.EnsureHelperFiles();

        Assert.True(changed);
        Assert.True(File.Exists(paths.SharedHelperCommandPath));
        Assert.Equal(
            "@echo off\r\n\"%~dp0..\\WindowsTerminal.exe\" %*\r\n",
            File.ReadAllText(paths.SharedHelperCommandPath));
    }

    [Fact]
    public void EnsureExecutableCopies_WhenVariantsAreMissing_CreatesManagedExecutableCopies()
    {
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.SharedRootPath);
        File.WriteAllText(paths.SharedExecutablePath, "managed");
        var programDataService = new FakeProgramDataSecurityService(_tempDirectory.Path);
        var service = CreateService(paths, programDataService);

        var changed = service.EnsureExecutableCopies();

        Assert.True(changed);
        foreach (var executablePath in paths.GetSharedExecutablePaths().Skip(1))
        {
            Assert.Equal("managed", File.ReadAllText(executablePath));
        }
    }

    [Fact]
    public void EnsureExecutableCopies_WhenVariantsExist_RepairsSecurityWithoutReplacing()
    {
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.SharedRootPath);
        File.WriteAllText(paths.SharedExecutablePath, "managed");
        foreach (var executablePath in paths.GetSharedExecutablePaths().Skip(1))
            File.Copy(paths.SharedExecutablePath, executablePath);
        var programDataService = new FakeProgramDataSecurityService(_tempDirectory.Path);
        var service = CreateService(paths, programDataService);

        var changed = service.EnsureExecutableCopies();

        Assert.False(changed);
        foreach (var executablePath in paths.GetSharedExecutablePaths().Skip(1))
        {
            Assert.Contains(executablePath, programDataService.EnsuredManagedFiles);
        }
    }

    [Fact]
    public void EnsureExecutableCopies_WhenMatchingVariantRepairChangesDacl_ReturnsTrue()
    {
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.SharedRootPath);
        File.WriteAllText(paths.SharedExecutablePath, "managed");
        foreach (var executablePath in paths.GetSharedExecutablePaths().Skip(1))
            File.Copy(paths.SharedExecutablePath, executablePath);
        var programDataService = new FakeProgramDataSecurityService(_tempDirectory.Path)
        {
            OwnerRepairChanges = true
        };
        var service = CreateService(paths, programDataService);

        var changed = service.EnsureExecutableCopies();

        Assert.True(changed);
        foreach (var executablePath in paths.GetSharedExecutablePaths().Skip(1))
        {
            Assert.Contains(executablePath, programDataService.EnsuredManagedFiles);
        }
    }

    [Fact]
    public void EnsureExecutableCopies_WhenVariantContentDiffers_ReplacesManagedExecutableCopy()
    {
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.SharedRootPath);
        File.WriteAllText(paths.SharedExecutablePath, "managed");
        var variantPath = paths.GetSharedExecutablePath(RunFence.Core.Models.PrivilegeLevel.Isolated);
        File.WriteAllText(variantPath, "stale");
        var programDataService = new FakeProgramDataSecurityService(_tempDirectory.Path);
        var service = CreateService(paths, programDataService);

        var changed = service.EnsureExecutableCopies();

        Assert.True(changed);
        Assert.Equal("managed", File.ReadAllText(variantPath));
    }

    [Fact]
    public void EnsureHelperFiles_WhenHelperContentDiffers_ReplacesManagedHelperFile()
    {
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.SharedHelperPathDirectory);
        File.WriteAllText(paths.SharedHelperCommandPath, "wrong");
        var programDataService = new FakeProgramDataSecurityService(_tempDirectory.Path);
        var service = CreateService(paths, programDataService);

        var changed = service.EnsureHelperFiles();

        Assert.True(changed);
        Assert.Contains(paths.SharedHelperCommandPath, programDataService.EnsuredManagedFiles);
        Assert.Equal("@echo off\r\n\"%~dp0..\\WindowsTerminal.exe\" %*\r\n", File.ReadAllText(paths.SharedHelperCommandPath));
    }

    [Fact]
    public void EnsureHelperFiles_WhenHelperAlreadyMatches_RepairsSecurityWithoutReplacing()
    {
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.SharedHelperPathDirectory);
        File.WriteAllText(paths.SharedHelperCommandPath, "@echo off\r\n\"%~dp0..\\WindowsTerminal.exe\" %*\r\n");
        var programDataService = new FakeProgramDataSecurityService(_tempDirectory.Path);
        var service = CreateService(paths, programDataService);

        var changed = service.EnsureHelperFiles();

        Assert.False(changed);
        Assert.Contains(paths.SharedHelperCommandPath, programDataService.EnsuredManagedFiles);
    }

    [Fact]
    public void EnsureHelperFiles_WhenMatchingHelperRepairChangesDacl_ReturnsTrue()
    {
        var paths = CreatePaths();
        Directory.CreateDirectory(paths.SharedHelperPathDirectory);
        File.WriteAllText(paths.SharedHelperCommandPath, "@echo off\r\n\"%~dp0..\\WindowsTerminal.exe\" %*\r\n");
        var programDataService = new FakeProgramDataSecurityService(_tempDirectory.Path)
        {
            OwnerRepairChanges = true
        };
        var service = CreateService(paths, programDataService);

        var changed = service.EnsureHelperFiles();

        Assert.True(changed);
        Assert.Contains(paths.SharedHelperCommandPath, programDataService.EnsuredManagedFiles);
    }

    public void Dispose()
    {
        _tempDirectory.Dispose();
    }

    private WindowsTerminalDeploymentPaths CreatePaths()
        => new(new TestProgramDataKnownPathResolver(_tempDirectory.Path));

    private static WindowsTerminalSharedDeploymentSecurityService CreateService(
        WindowsTerminalDeploymentPaths paths,
        FakeProgramDataSecurityService programDataService)
        => new(programDataService, programDataService, paths);

    private sealed class FakeProgramDataSecurityService(string rootPath)
        : IProgramDataDirectoryProvisioningService,
            IProgramDataManagedObjectRepairService
    {
        public List<string> EnsuredDirectories { get; } = [];
        public List<string> EnsuredManagedFiles { get; } = [];
        public bool OwnerRepairChanges { get; init; }

        public string EnsureRoot() => throw new NotSupportedException();

        public string EnsureSubdirectory(string relativePath, ProgramDataDirectoryAclProfile aclProfile)
            => throw new NotSupportedException();

        public string EnsureKnownDirectory(ProgramDataDirectoryPolicy policy)
        {
            var path = Path.Combine(rootPath, policy.RelativePath);
            EnsuredDirectories.Add(path);
            Directory.CreateDirectory(path);
            return path;
        }

        public void EnsureKnownDirectoryTreeInheritsFromRoot(ProgramDataDirectoryPolicy policy)
        {
            var path = Path.Combine(rootPath, policy.RelativePath);
            EnsuredDirectories.Add(path);
            Directory.CreateDirectory(path);
        }

        public void EnsureDirectoryUnderRoot(string directoryPath, ProgramDataDirectoryAclProfile aclProfile)
        {
            EnsuredDirectories.Add(directoryPath);
            Directory.CreateDirectory(directoryPath);
        }

        public void EnsureDirectoryTreeInheritsFromRoot(
            string directoryPath,
            ProgramDataDirectoryAclProfile rootAclProfile)
            => throw new NotSupportedException();

        public void EnsureTraverseOnlyAccess(
            string directoryPath,
            string sid,
            ProgramDataDirectoryAclProfile aclProfile)
            => throw new NotSupportedException();

        public bool EnsureManagedFileOwner(string filePath)
        {
            EnsuredManagedFiles.Add(filePath);
            return OwnerRepairChanges;
        }

        public bool EnsureManagedDirectoryOwner(string directoryPath)
            => OwnerRepairChanges;

        public ProgramDataSecurityRepairResult EnsureManagedFileSecurity(string filePath, ProgramDataFileAclProfile aclProfile)
        {
            EnsuredManagedFiles.Add(filePath);
            return new ProgramDataSecurityRepairResult(OwnerRepairChanges, false, false);
        }

        public bool EnsureManagedFileOwner(string filePath, IReadOnlyCollection<string> expectedAdditionalOwnerSids)
            => EnsureManagedFileOwner(filePath);

        public bool EnsureManagedDirectoryOwner(
            string directoryPath,
            IReadOnlyCollection<string> expectedAdditionalOwnerSids)
            => EnsureManagedDirectoryOwner(directoryPath);
    }
}
