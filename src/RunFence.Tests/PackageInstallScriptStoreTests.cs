using System.Security.AccessControl;
using System.Security.Principal;
using System.Linq;
using Moq;
using RunFence.Account.UI;
using RunFence.Acl;
using RunFence.Core;
using Xunit;

namespace RunFence.Tests;

public class PackageInstallScriptStoreTests : IDisposable
{
    private readonly TempDirectory _tempDirectory = new("RunFence_PackageInstallScriptStore");
    private readonly List<string> _pathsToDelete = [];
    private readonly Mock<IProgramDataDirectoryProvisioningService> _securityService = new();
    private readonly Mock<IProgramDataObjectProvisioner> _objectProvisioner = new();
    private readonly Mock<IProgramDataKnownPathResolver> _pathResolver = new();
    private readonly List<ProgramDataExplicitFileRequest> _explicitFileRequests = [];
    private readonly string _testSid = WindowsIdentity.GetCurrent().User!.Value;
    private string ProgramDataRoot => _tempDirectory.Path;

    private PackageInstallScriptStore CreateStore()
    {
        _objectProvisioner
            .Setup(p => p.CreateFile(It.IsAny<ProgramDataExplicitFileRequest>(), It.IsAny<Action<Stream>>()))
            .Callback<ProgramDataExplicitFileRequest, Action<Stream>>((request, writeContent) =>
            {
                _explicitFileRequests.Add(request);
                Directory.CreateDirectory(Path.GetDirectoryName(request.Path)!);
                using var stream = new FileStream(request.Path, FileMode.CreateNew, FileAccess.ReadWrite, request.Share);
                writeContent(stream);
            });
        return new(
            Mock.Of<ILoggingService>(),
            _securityService.Object,
            _objectProvisioner.Object,
            _pathResolver.Object);
    }

    private string SetupScriptsDir()
    {
        var scriptsDir = Path.Combine(ProgramDataRoot, ProgramDataPolicies.PackageInstallScripts.RelativePath);
        Directory.CreateDirectory(ProgramDataRoot);
        Directory.CreateDirectory(scriptsDir);

        _pathResolver
            .Setup(resolver => resolver.GetDirectoryPath(ProgramDataPolicies.PackageInstallScripts))
            .Returns(scriptsDir);
        _securityService.Setup(s => s.EnsureRoot()).Returns(ProgramDataRoot);
        _securityService.Setup(s => s.EnsureKnownDirectory(ProgramDataPolicies.PackageInstallScripts))
            .Returns(scriptsDir);

        return scriptsDir;
    }

    [Fact]
    public void CreateScript_UsesPackageInstallScriptsDirectoryAndSecuresNewFile()
    {
        var scriptsDir = SetupScriptsDir();
        _securityService.Setup(s => s.EnsureTraverseOnlyAccess(scriptsDir, _testSid, ProgramDataDirectoryAclProfile.TrustedOnly));

        var store = CreateStore();
        var scriptPath = store.CreateScript("@\"Write-Output hi\"@", _testSid);
        _pathsToDelete.Add(scriptPath);

        Assert.Contains(ProgramDataPolicies.PackageInstallScripts.RelativePath, scriptPath);
        Assert.True(File.Exists(scriptPath));

        var request = Assert.Single(_explicitFileRequests);
        Assert.Equal(scriptPath, request.Path);
        Assert.Equal(ProgramDataFileAclProfile.TrustedOnly, request.Profile);
        Assert.Equal(FileShare.Read, request.Share);
        Assert.Contains(request.AdditionalAccess, access =>
            access.Principal.Value == _testSid &&
            (access.Rights & FileSystemRights.ReadAndExecute) != 0 &&
            (access.Rights & FileSystemRights.Delete) != 0);

        _securityService.Verify(s => s.EnsureRoot(), Times.Exactly(2));
        _securityService.Verify(
            s => s.EnsureKnownDirectory(ProgramDataPolicies.PackageInstallScripts),
            Times.Exactly(2));
        _securityService.Verify(
            s => s.EnsureTraverseOnlyAccess(scriptsDir, _testSid, ProgramDataDirectoryAclProfile.TrustedOnly),
            Times.Once);
    }

    [Fact]
    public void CleanupStaleScripts_RemovesLegacyAndPackageInstallScriptsButPreservesFreshOrNonInstallFiles()
    {
        var scriptsDir = SetupScriptsDir();
        var legacyRoot = Path.Combine(ProgramDataRoot, $"install-{Guid.NewGuid():N}.ps1");
        var packageRoot = Path.Combine(scriptsDir, $"install-{Guid.NewGuid():N}.ps1");
        var freshFile = Path.Combine(ProgramDataRoot, $"install-{Guid.NewGuid():N}.ps1");
        var nonInstallFile = Path.Combine(ProgramDataRoot, $"notes-{Guid.NewGuid():N}.ps1");
        var packageNonInstall = Path.Combine(scriptsDir, "other-note.ps1");

        File.WriteAllText(legacyRoot, "legacy");
        File.WriteAllText(packageRoot, "package");
        File.WriteAllText(freshFile, "fresh");
        File.WriteAllText(nonInstallFile, "note");
        File.WriteAllText(packageNonInstall, "note2");
        File.SetCreationTimeUtc(legacyRoot, DateTime.UtcNow.AddHours(-2));
        File.SetCreationTimeUtc(packageRoot, DateTime.UtcNow.AddHours(-2));
        File.SetCreationTimeUtc(freshFile, DateTime.UtcNow);
        _pathsToDelete.AddRange([legacyRoot, packageRoot, freshFile, nonInstallFile, packageNonInstall]);

        var store = CreateStore();

        store.CleanupStaleScripts();

        Assert.False(File.Exists(legacyRoot));
        Assert.False(File.Exists(packageRoot));
        Assert.True(File.Exists(freshFile));
        Assert.True(File.Exists(nonInstallFile));
        Assert.True(File.Exists(packageNonInstall));
        _securityService.Verify(s => s.EnsureRoot(), Times.Once);
        _securityService.Verify(
            s => s.EnsureKnownDirectory(ProgramDataPolicies.PackageInstallScripts),
            Times.Once);
    }

    [Fact]
    public void CreateScript_RefreshesRootAndFolderSecurityBeforeWrite()
    {
        var scriptsDir = SetupScriptsDir();
        _securityService.Setup(s => s.EnsureTraverseOnlyAccess(scriptsDir, _testSid, ProgramDataDirectoryAclProfile.TrustedOnly));

        var store = CreateStore();
        var staleFile = Path.Combine(ProgramDataRoot, $"install-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(staleFile, "stale");
        File.SetCreationTimeUtc(staleFile, DateTime.UtcNow.AddHours(-2));
        _pathsToDelete.Add(staleFile);

        var scriptPath = store.CreateScript("echo hi", _testSid);
        _pathsToDelete.Add(scriptPath);

        Assert.False(File.Exists(staleFile));
        Assert.True(File.Exists(scriptPath));
        Assert.Contains(ProgramDataPolicies.PackageInstallScripts.RelativePath, scriptPath);
    }

    public void Dispose()
    {
        foreach (var path in _pathsToDelete)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }

        _tempDirectory.Dispose();
    }
}
