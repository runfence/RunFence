using Moq;
using RunFence.Account.UI;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public sealed class WindowsTerminalDeploymentConsoleRunnerTests
{
    [Fact]
    public async Task DownloadAsync_WhenAppIsLockedBeforeScriptCreation_DoesNotRunPowerShell()
    {
        var processExecutionService = new Mock<IProcessExecutionService>(MockBehavior.Strict);
        var appLock = new Mock<IAppLockControl>();
        appLock.Setup(control => control.IsLocked).Returns(true);
        var runner = new WindowsTerminalDeploymentConsoleRunner(
            processExecutionService.Object,
            new WindowsTerminalDeploymentScriptBuilder(),
            appLock.Object);

        await Assert.ThrowsAsync<OperationCanceledException>(() => runner.DownloadAsync(
            new WindowsTerminalPackageDownloadOperation("https://example.invalid/terminal.zip", Path.Combine(Path.GetTempPath(), "terminal.zip")),
            CancellationToken.None));

        processExecutionService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DownloadAsync_WhenAppLocksAfterScriptCreation_DoesNotRunPowerShell()
    {
        var processExecutionService = new Mock<IProcessExecutionService>(MockBehavior.Strict);
        var appLock = new Mock<IAppLockControl>();
        appLock
            .SetupSequence(control => control.IsLocked)
            .Returns(false)
            .Returns(true);
        var runner = new WindowsTerminalDeploymentConsoleRunner(
            processExecutionService.Object,
            new WindowsTerminalDeploymentScriptBuilder(),
            appLock.Object);

        await Assert.ThrowsAsync<OperationCanceledException>(() => runner.DownloadAsync(
            new WindowsTerminalPackageDownloadOperation("https://example.invalid/terminal.zip", Path.Combine(Path.GetTempPath(), "terminal.zip")),
            CancellationToken.None));

        processExecutionService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DownloadAsync_WhenAppIsUnlocked_RunsPowerShell()
    {
        var processExecutionService = new Mock<IProcessExecutionService>();
        processExecutionService
            .Setup(service => service.RunAsync(It.IsAny<ProcessExecutionRequest>()))
            .ReturnsAsync(new ProcessExecutionResult(true, 0, false, string.Empty, string.Empty, null));
        var appLock = new Mock<IAppLockControl>();
        var runner = new WindowsTerminalDeploymentConsoleRunner(
            processExecutionService.Object,
            new WindowsTerminalDeploymentScriptBuilder(),
            appLock.Object);

        await runner.DownloadAsync(
            new WindowsTerminalPackageDownloadOperation("https://example.invalid/terminal.zip", Path.Combine(Path.GetTempPath(), "terminal.zip")),
            CancellationToken.None);

        processExecutionService.Verify(
            service => service.RunAsync(It.Is<ProcessExecutionRequest>(request =>
                request.UseShellExecute &&
                request.WindowStyle == System.Diagnostics.ProcessWindowStyle.Normal &&
                request.Arguments.Contains("-File", StringComparison.Ordinal))),
            Times.Once);
    }
}
