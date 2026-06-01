using Moq;
using RunFence.Account.UI;
using RunFence.Acl;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public sealed class WindowsTerminalPrivilegeLaunchExecutableServiceTests : IDisposable
{
    private readonly TempDirectory _tempDirectory = new("RunFence_WindowsTerminalPrivilegeLaunchExecutable");
    private readonly ManualClock _clock = new(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

    [Theory]
    [InlineData(PrivilegeLevel.Isolated, "WindowsTerminal-Isolated-")]
    [InlineData(PrivilegeLevel.LowIntegrity, "WindowsTerminal-LowIntegrity-")]
    public void PrepareLaunchExecutablePath_CreatesShortRandomHardLinkAndDeletesExistingLaunchCopy(
        PrivilegeLevel privilegeLevel,
        string expectedFileNamePrefix)
    {
        var deploymentPaths = new WindowsTerminalDeploymentPaths(new TestProgramDataKnownPathResolver(_tempDirectory.Path));
        Directory.CreateDirectory(deploymentPaths.SharedRootPath);
        File.WriteAllText(deploymentPaths.SharedExecutablePath, "base");
        File.WriteAllText(deploymentPaths.GetSharedExecutablePath(privilegeLevel), "initial");
        var existingLaunchPath = deploymentPaths.CreatePrivilegeLaunchExecutablePath(privilegeLevel);
        File.WriteAllText(existingLaunchPath, "existing");
        var pathPolicyService = new Mock<IProgramDataPathPolicyService>();
        pathPolicyService.Setup(policy => policy.IsUnderRoot(It.IsAny<string>())).Returns(true);

        var service = new WindowsTerminalPrivilegeLaunchExecutableService(
            deploymentPaths,
            pathPolicyService.Object,
            _clock,
            Mock.Of<ILoggingService>());

        var launchExecutablePath = service.PrepareLaunchExecutablePath(privilegeLevel);

        Assert.StartsWith(deploymentPaths.SharedRootPath, launchExecutablePath, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(@$"{expectedFileNamePrefix}[0-9a-f]{{8}}\.exe$", Path.GetFileName(launchExecutablePath));
        Assert.True(deploymentPaths.IsSharedExecutablePath(launchExecutablePath));
        Assert.False(File.Exists(existingLaunchPath));

        File.WriteAllText(deploymentPaths.GetSharedExecutablePath(privilegeLevel), "updated");
        Assert.Equal("updated", File.ReadAllText(launchExecutablePath));
    }

    [Theory]
    [InlineData(PrivilegeLevel.Isolated)]
    [InlineData(PrivilegeLevel.LowIntegrity)]
    public void PrepareLaunchExecutablePath_WhenPrivilegeExecutableIsMissing_FallsBackToSharedExecutable(PrivilegeLevel privilegeLevel)
    {
        var deploymentPaths = new WindowsTerminalDeploymentPaths(new TestProgramDataKnownPathResolver(_tempDirectory.Path));
        Directory.CreateDirectory(deploymentPaths.SharedRootPath);
        File.WriteAllText(deploymentPaths.SharedExecutablePath, "base");
        var pathPolicyService = new Mock<IProgramDataPathPolicyService>();
        pathPolicyService.Setup(policy => policy.IsUnderRoot(It.IsAny<string>())).Returns(true);

        var service = new WindowsTerminalPrivilegeLaunchExecutableService(
            deploymentPaths,
            pathPolicyService.Object,
            _clock,
            Mock.Of<ILoggingService>());

        var launchExecutablePath = service.PrepareLaunchExecutablePath(privilegeLevel);

        Assert.Equal(deploymentPaths.SharedExecutablePath, launchExecutablePath);
    }

    [Theory]
    [InlineData(PrivilegeLevel.Isolated)]
    [InlineData(PrivilegeLevel.LowIntegrity)]
    public void PrepareLaunchExecutablePath_DoesNotDeleteRecentPreparedLaunchCopy(PrivilegeLevel privilegeLevel)
    {
        var deploymentPaths = new WindowsTerminalDeploymentPaths(new TestProgramDataKnownPathResolver(_tempDirectory.Path));
        Directory.CreateDirectory(deploymentPaths.SharedRootPath);
        File.WriteAllText(deploymentPaths.SharedExecutablePath, "base");
        File.WriteAllText(deploymentPaths.GetSharedExecutablePath(privilegeLevel), "initial");
        var pathPolicyService = new Mock<IProgramDataPathPolicyService>();
        pathPolicyService.Setup(policy => policy.IsUnderRoot(It.IsAny<string>())).Returns(true);
        var service = new WindowsTerminalPrivilegeLaunchExecutableService(
            deploymentPaths,
            pathPolicyService.Object,
            _clock,
            Mock.Of<ILoggingService>());

        var firstLaunchExecutablePath = service.PrepareLaunchExecutablePath(privilegeLevel);
        var secondLaunchExecutablePath = service.PrepareLaunchExecutablePath(privilegeLevel);

        Assert.NotEqual(firstLaunchExecutablePath, secondLaunchExecutablePath);
        Assert.True(File.Exists(firstLaunchExecutablePath));
        Assert.True(File.Exists(secondLaunchExecutablePath));
    }

    [Theory]
    [InlineData(PrivilegeLevel.Isolated)]
    [InlineData(PrivilegeLevel.LowIntegrity)]
    public void PrepareLaunchExecutablePath_DeletesPreparedLaunchCopyAfterRetention(PrivilegeLevel privilegeLevel)
    {
        var deploymentPaths = new WindowsTerminalDeploymentPaths(new TestProgramDataKnownPathResolver(_tempDirectory.Path));
        Directory.CreateDirectory(deploymentPaths.SharedRootPath);
        File.WriteAllText(deploymentPaths.SharedExecutablePath, "base");
        File.WriteAllText(deploymentPaths.GetSharedExecutablePath(privilegeLevel), "initial");
        var pathPolicyService = new Mock<IProgramDataPathPolicyService>();
        pathPolicyService.Setup(policy => policy.IsUnderRoot(It.IsAny<string>())).Returns(true);
        var service = new WindowsTerminalPrivilegeLaunchExecutableService(
            deploymentPaths,
            pathPolicyService.Object,
            _clock,
            Mock.Of<ILoggingService>());

        var firstLaunchExecutablePath = service.PrepareLaunchExecutablePath(privilegeLevel);
        _clock.UtcNow += TimeSpan.FromDays(2);

        var secondLaunchExecutablePath = service.PrepareLaunchExecutablePath(privilegeLevel);

        Assert.NotEqual(firstLaunchExecutablePath, secondLaunchExecutablePath);
        Assert.False(File.Exists(firstLaunchExecutablePath));
        Assert.True(File.Exists(secondLaunchExecutablePath));
    }

    public void Dispose()
    {
        _tempDirectory.Dispose();
    }

    private sealed class ManualClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; set; } = utcNow;
    }
}
