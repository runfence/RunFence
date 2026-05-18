using Moq;
using RunFence.Firewall;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class NetshCommandRunnerTests
{
    [Fact]
    public async Task RunAsync_UsesSharedProcessExecutionRequest_AndMapsFailureResult()
    {
        var processExecutionService = new Mock<IProcessExecutionService>(MockBehavior.Strict);
        ProcessExecutionRequest? capturedRequest = null;
        processExecutionService
            .Setup(s => s.RunAsync(It.IsAny<ProcessExecutionRequest>()))
            .Callback<ProcessExecutionRequest>(request => capturedRequest = request)
            .ReturnsAsync(new ProcessExecutionResult(
                Started: true,
                ExitCode: 5,
                TimedOut: false,
                StandardOutput: "netsh output",
                StandardError: " access denied ",
                FailureMessage: null));
        var runner = new NetshCommandRunner(processExecutionService.Object);

        var result = await runner.RunAsync("int ipv4 show dynamicport tcp");

        Assert.NotNull(capturedRequest);
        Assert.Equal("netsh", capturedRequest!.FileName);
        Assert.Equal("int ipv4 show dynamicport tcp", capturedRequest.Arguments);
        Assert.Equal(TimeSpan.FromSeconds(5), capturedRequest.Timeout);
        Assert.True(capturedRequest.KillEntireProcessTreeOnTimeout);
        Assert.True(capturedRequest.RedirectStandardOutput);
        Assert.True(capturedRequest.RedirectStandardError);
        Assert.Equal(5, result.ExitCode);
        Assert.Equal("netsh output", result.StandardOutput);
        Assert.False(result.TimedOut);
        Assert.Equal("access denied", result.FailureMessage);
    }

    [Fact]
    public async Task RunAsync_Timeout_ReturnsExistingTimedOutResult()
    {
        var runner = new NetshCommandRunner(MockProcessExecutionService(new ProcessExecutionResult(
            Started: true,
            ExitCode: null,
            TimedOut: true,
            StandardOutput: "ignored",
            StandardError: "ignored",
            FailureMessage: null)));

        var result = await runner.RunAsync("int ipv4 show dynamicport tcp");

        Assert.Equal(-1, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardOutput);
        Assert.True(result.TimedOut);
        Assert.Equal("netsh timed out.", result.FailureMessage);
    }

    [Fact]
    public async Task RunAsync_StartFailure_ThrowsInvalidOperationException()
    {
        var runner = new NetshCommandRunner(MockProcessExecutionService(new ProcessExecutionResult(
            Started: false,
            ExitCode: null,
            TimedOut: false,
            StandardOutput: string.Empty,
            StandardError: string.Empty,
            FailureMessage: "start failed")));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync("int ipv4 show dynamicport tcp"));

        Assert.Equal("start failed", exception.Message);
    }

    private static IProcessExecutionService MockProcessExecutionService(ProcessExecutionResult result)
    {
        var service = new Mock<IProcessExecutionService>(MockBehavior.Strict);
        service.Setup(s => s.RunAsync(It.IsAny<ProcessExecutionRequest>())).ReturnsAsync(result);
        return service.Object;
    }
}
