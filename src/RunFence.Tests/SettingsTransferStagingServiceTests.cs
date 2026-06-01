using System.Security.AccessControl;
using Moq;
using RunFence.Acl;
using RunFence.Core;
using RunFence.PrefTrans;
using Xunit;

namespace RunFence.Tests;

public class SettingsTransferStagingServiceTests
{
    private const string InteractiveSid = "S-1-5-21-2222-2222-2222-2222";
    private static readonly string SharedTempRoot = Path.Combine(PathConstants.ProgramDataDir, ProgramDataPolicies.Temp.RelativePath);
    private readonly Mock<IProgramDataDirectoryProvisioningService> _programDataDirectorySecurityService;
    private readonly Mock<IProgramDataObjectProvisioner> _programDataObjectProvisioner;
    private readonly Mock<IProgramDataKnownPathResolver> _programDataKnownPathResolver;
    private readonly SettingsTransferStagingService _service;

    public SettingsTransferStagingServiceTests()
    {
        _programDataDirectorySecurityService = new(MockBehavior.Strict);
        _programDataObjectProvisioner = new(MockBehavior.Strict);
        _programDataKnownPathResolver = new(MockBehavior.Strict);
        _programDataKnownPathResolver
            .Setup(resolver => resolver.GetDirectoryPath(ProgramDataPolicies.Temp))
            .Returns(SharedTempRoot);
        _programDataDirectorySecurityService
            .Setup(s => s.EnsureRoot())
            .Returns(PathConstants.ProgramDataDir);
        _programDataDirectorySecurityService
            .Setup(s => s.EnsureKnownDirectory(ProgramDataPolicies.Temp))
            .Returns(SharedTempRoot);
        _programDataDirectorySecurityService
            .Setup(s => s.EnsureTraverseOnlyAccess(SharedTempRoot, InteractiveSid, ProgramDataDirectoryAclProfile.TrustedOnly));
        _programDataObjectProvisioner
            .Setup(p => p.CreateOrRepairDirectory(It.IsAny<ProgramDataExplicitDirectoryRequest>()))
            .Callback<ProgramDataExplicitDirectoryRequest>(request => Directory.CreateDirectory(request.Path));

        _service = new SettingsTransferStagingService(
            _programDataDirectorySecurityService.Object,
            _programDataObjectProvisioner.Object,
            _programDataKnownPathResolver.Object);
    }

    [Fact]
    public void CopyImportFileToRestrictedTemp_CreatesRestrictedParentDirectoryAndPreservesContent()
    {
        using var sourceDir = new TempDirectory("SettingsTransferStagingTestsSource");
        var sourceFile = Path.Combine(sourceDir.Path, "source.json");
        File.WriteAllText(sourceFile, "{\"ok\":true}");

        var destination = _service.CreateSharedTempFilePath("json");
        var destinationDir = Path.GetDirectoryName(destination)!;

        var returned = _service.CopyImportFileToRestrictedTemp(
            sourceFile,
            destination,
            InteractiveSid);

        Assert.Equal(destination, returned);
        Assert.Equal("{\"ok\":true}", File.ReadAllText(destination));
        Assert.True(Directory.Exists(destinationDir));

        _programDataDirectorySecurityService.Verify(s => s.EnsureRoot(), Times.Once);
        _programDataDirectorySecurityService.Verify(s => s.EnsureKnownDirectory(ProgramDataPolicies.Temp), Times.Once);
        _programDataDirectorySecurityService.Verify(s => s.EnsureTraverseOnlyAccess(SharedTempRoot, InteractiveSid, ProgramDataDirectoryAclProfile.TrustedOnly), Times.Once);
        _programDataObjectProvisioner.Verify(p => p.CreateOrRepairDirectory(
            It.Is<ProgramDataExplicitDirectoryRequest>(request =>
                request.Path == destinationDir &&
                request.Profile == ProgramDataDirectoryAclProfile.CurrentProcessUserFullControl &&
                request.ReplaceExistingSecurity &&
                request.AdditionalAccess.Count == 1 &&
                request.AdditionalAccess[0].Principal.Value == InteractiveSid &&
                request.AdditionalAccess[0].Rights.HasFlag(FileSystemRights.Modify))),
            Times.Once);

        _service.TryDeleteTempDirectory(destinationDir);
    }

    [Fact]
    public void TryDeleteTempFile_AndDirectory_RemovePaths()
    {
        var directory = _service.CreateSharedTempDirectoryPath();
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "temp.json");
        File.WriteAllText(file, "x");

        var deletedFile = _service.TryDeleteTempFile(file);
        var deletedDir = _service.TryDeleteTempDirectory(directory);

        Assert.Equal(file, deletedFile);
        Assert.False(Directory.Exists(directory));
        Assert.Equal(directory, deletedDir);
    }

}
