using System.Security.AccessControl;
using System.Security.Principal;
using Moq;
using RunFence.Acl;
using RunFence.Core;
using RunFence.PrefTrans;
using Xunit;

namespace RunFence.Tests;

public class PrefTransLogWorkspaceTests
{
    private readonly Mock<IProgramDataDirectoryProvisioningService> _programDataSecurityService;
    private readonly Mock<IProgramDataObjectProvisioner> _programDataObjectProvisioner;
    private readonly Mock<IProgramDataKnownPathResolver> _programDataKnownPathResolver;

    public PrefTransLogWorkspaceTests()
    {
        _programDataSecurityService = new Mock<IProgramDataDirectoryProvisioningService>(MockBehavior.Strict);
        _programDataObjectProvisioner = new Mock<IProgramDataObjectProvisioner>(MockBehavior.Strict);
        _programDataKnownPathResolver = new Mock<IProgramDataKnownPathResolver>(MockBehavior.Strict);
        _programDataKnownPathResolver
            .Setup(resolver => resolver.GetDirectoryPath(ProgramDataPolicies.RunFencePrefTransLogs))
            .Returns(Path.Combine(PathConstants.ProgramDataDir, ProgramDataPolicies.RunFencePrefTransLogs.RelativePath));
        _programDataObjectProvisioner
            .Setup(p => p.CreateFile(It.IsAny<ProgramDataExplicitFileRequest>(), It.IsAny<Action<Stream>>()))
            .Callback<ProgramDataExplicitFileRequest, Action<Stream>>((request, writeContent) =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(request.Path)!);
                using var stream = new FileStream(request.Path, FileMode.CreateNew, FileAccess.ReadWrite, request.Share);
                writeContent(stream);
            });
    }

    [Fact]
    public void CreateLogFile_ReadLogFile_AndTryDeleteLogFile_WorkEndToEnd()
    {
        var log = new Mock<ILoggingService>();
        var accountSid = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("Current user SID is unavailable.");
        var workspace = new PrefTransLogWorkspace(
            log.Object,
            _programDataSecurityService.Object,
            _programDataObjectProvisioner.Object,
            _programDataKnownPathResolver.Object);
        var logDir = Path.Combine(PathConstants.ProgramDataDir, ProgramDataPolicies.RunFencePrefTransLogs.RelativePath);

        _programDataSecurityService.Setup(s => s.EnsureRoot()).Returns(PathConstants.ProgramDataDir);
        _programDataSecurityService.Setup(
                s => s.EnsureKnownDirectory(ProgramDataPolicies.RunFencePrefTransLogs))
            .Returns(logDir);
        _programDataSecurityService.Setup(s => s.EnsureTraverseOnlyAccess(logDir, accountSid, ProgramDataDirectoryAclProfile.TrustedOnly));

        var result = workspace.CreateLogFile(accountSid);
        Assert.True(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.LogFilePath));

        var logFilePath = result.LogFilePath!;
        try
        {
            Assert.True(File.Exists(logFilePath));

            File.AppendAllText(logFilePath, "  preftrans failure  ");
            var content = workspace.ReadLogFile(logFilePath);

            Assert.Equal("preftrans failure", content);
        }
        finally
        {
            workspace.TryDeleteLogFile(logFilePath);
            Assert.False(File.Exists(logFilePath));
        }

        _programDataSecurityService.Verify(s => s.EnsureRoot(), Times.Once);
        _programDataSecurityService.Verify(s => s.EnsureKnownDirectory(ProgramDataPolicies.RunFencePrefTransLogs), Times.Once);
        _programDataSecurityService.Verify(s => s.EnsureTraverseOnlyAccess(logDir, accountSid, ProgramDataDirectoryAclProfile.TrustedOnly), Times.Once);
        _programDataObjectProvisioner.Verify(p => p.CreateFile(
            It.Is<ProgramDataExplicitFileRequest>(request =>
                request.Path == logFilePath &&
                request.Profile == ProgramDataFileAclProfile.TrustedOnly &&
                request.Share == FileShare.ReadWrite &&
                request.AdditionalAccess.Count == 1 &&
                request.AdditionalAccess[0].Principal.Value == accountSid &&
                request.AdditionalAccess[0].Rights.HasFlag(FileSystemRights.WriteData)),
            It.IsAny<Action<Stream>>()),
            Times.Once);
    }

    [Fact]
    public void ReadLogFile_WhenFileIsMissing_ReturnsEmptyString()
    {
        var log = new Mock<ILoggingService>();
        var workspace = new PrefTransLogWorkspace(
            log.Object,
            _programDataSecurityService.Object,
            _programDataObjectProvisioner.Object,
            _programDataKnownPathResolver.Object);

        var result = workspace.ReadLogFile(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".log"));

        Assert.Equal(string.Empty, result);
    }
}
