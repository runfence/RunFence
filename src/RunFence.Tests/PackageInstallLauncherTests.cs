using Moq;
using RunFence.Account.UI;
using RunFence.Launch;
using RunFence.Launch.Tokens;
using Xunit;

namespace RunFence.Tests;

public class PackageInstallLauncherTests
{
    [Fact]
    public void Launch_BuildsPowerShellWrapperAndReturnsWarningsAndDetachedProcess()
    {
        ProcessLaunchTarget? capturedTarget = null;
        AccountLaunchIdentity? capturedIdentity = null;
        var launchFacade = new Mock<ILaunchFacade>();
        launchFacade
            .Setup(f => f.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), It.IsAny<Func<string, string, bool>?>()))
            .Callback<ProcessLaunchTarget, LaunchIdentity, Func<string, string, bool>?>((target, identity, _) =>
            {
                capturedTarget = target;
                capturedIdentity = Assert.IsType<AccountLaunchIdentity>(identity);
            })
            .Returns(new LaunchExecutionResult(
                LaunchExecutionStatus.ProcessStarted,
                ProcessInfo.FromManagedProcess(null),
                ["warning"]));

        var launcher = new PackageInstallLauncher(launchFacade.Object);
        var identity = new AccountLaunchIdentity("S-1-5-21-1");

        var result = launcher.Launch(@"C:\Temp\install'script.ps1", identity);

        Assert.Equal(identity, capturedIdentity);
        Assert.NotNull(capturedTarget);
        Assert.Equal("powershell.exe", capturedTarget!.ExePath);
        Assert.Contains("-ExecutionPolicy", capturedTarget.Arguments, StringComparison.Ordinal);
        Assert.Contains("Bypass", capturedTarget.Arguments, StringComparison.Ordinal);
        Assert.Contains("Read-Host -Prompt", capturedTarget.Arguments, StringComparison.Ordinal);
        Assert.Contains("'C:\\Temp\\install''script.ps1'", capturedTarget.Arguments, StringComparison.Ordinal);
        Assert.Equal(["warning"], result.MaintenanceWarnings);
        Assert.True(result.Process.HasExited);
        result.Process.Dispose();
    }

    [Fact]
    public void Launch_WhenLaunchFileReturnsNoProcessHandle_ThrowsInvalidOperationException()
    {
        var launchFacade = new Mock<ILaunchFacade>();
        launchFacade
            .Setup(f => f.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), It.IsAny<Func<string, string, bool>?>()))
            .Returns(new LaunchExecutionResult(LaunchExecutionStatus.ProcessStarted, null));
        var launcher = new PackageInstallLauncher(launchFacade.Object);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            launcher.Launch(@"C:\Temp\install.ps1", new AccountLaunchIdentity("S-1-5-21-1")));

        Assert.Equal("Package install script did not return a process handle.", ex.Message);
    }
}
