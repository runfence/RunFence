using Moq;
using RunFence.Infrastructure;
using RunFence.Startup;
using Xunit;

namespace RunFence.Tests;

public class StartupSecurityScannerRunnerTests
{
    [Fact]
    public void Run_MissingScannerExecutable_ReturnsNotStartedFailure()
    {
        using var tempDir = new TempDirectory("RunFence_SecurityScannerRunner");
        var scannerPath = Path.Combine(tempDir.Path, "missing-scanner.exe");
        var processExecutionService = new Mock<IProcessExecutionService>(MockBehavior.Strict);
        var runner = new StartupSecurityScannerRunner(processExecutionService.Object, scannerPath);

        var result = runner.Run(CancellationToken.None);

        Assert.False(result.Started);
        Assert.False(result.TimedOut);
        Assert.Null(result.ExitCode);
        Assert.Contains(scannerPath, result.FailureMessage, StringComparison.Ordinal);
        processExecutionService.Verify(s => s.Run(It.IsAny<ProcessExecutionRequest>()), Times.Never);
    }

    [Fact]
    public void Run_ProcessStartFailure_PrefixesStartupScannerFailureMessage()
    {
        using var tempDir = new TempDirectory("RunFence_SecurityScannerRunner");
        var scannerPath = Path.Combine(tempDir.Path, "RunFence.SecurityScanner.exe");
        File.WriteAllBytes(scannerPath, []);

        var processExecutionService = new Mock<IProcessExecutionService>(MockBehavior.Strict);
        processExecutionService
            .Setup(s => s.Run(It.IsAny<ProcessExecutionRequest>()))
            .Returns(new ProcessExecutionResult(
                Started: false,
                ExitCode: null,
                TimedOut: false,
                StandardOutput: string.Empty,
                StandardError: string.Empty,
                FailureMessage: "denied"));
        var runner = new StartupSecurityScannerRunner(processExecutionService.Object, scannerPath);

        var result = runner.Run(CancellationToken.None);

        Assert.False(result.Started);
        Assert.Equal("Failed to start security scanner process: denied", result.FailureMessage);
    }

    [Fact]
    public void Run_ExistingScannerExecutable_UsesSharedProcessExecutionRequest()
    {
        using var tempDir = new TempDirectory("RunFence_SecurityScannerRunner");
        var scannerPath = Path.Combine(tempDir.Path, "RunFence.SecurityScanner.exe");
        File.WriteAllBytes(scannerPath, []);

        var expectedResult = new ProcessExecutionResult(
            Started: true,
            ExitCode: 0,
            TimedOut: false,
            StandardOutput: string.Empty,
            StandardError: string.Empty,
            FailureMessage: null);

        ProcessExecutionRequest? capturedRequest = null;
        using var cts = new CancellationTokenSource();
        var processExecutionService = new Mock<IProcessExecutionService>(MockBehavior.Strict);
        processExecutionService
            .Setup(s => s.Run(It.IsAny<ProcessExecutionRequest>()))
            .Callback<ProcessExecutionRequest>(request => capturedRequest = request)
            .Returns(expectedResult);
        var runner = new StartupSecurityScannerRunner(processExecutionService.Object, scannerPath);

        var result = runner.Run(cts.Token);

        Assert.NotNull(capturedRequest);
        Assert.Equal(scannerPath, capturedRequest!.FileName);
        Assert.Equal(string.Empty, capturedRequest.Arguments);
        Assert.Equal(TimeSpan.FromMinutes(2), capturedRequest.Timeout);
        Assert.True(capturedRequest.KillEntireProcessTreeOnTimeout);
        Assert.True(capturedRequest.RedirectStandardOutput);
        Assert.True(capturedRequest.RedirectStandardError);
        Assert.Equal(cts.Token, capturedRequest.CancellationToken);
        Assert.Equal(expectedResult, result);
    }
}
