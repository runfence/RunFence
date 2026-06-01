using Moq;
using RunFence.Core;
using RunFence.Firewall;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class AuditPolCommandRunnerTests
{
    [Fact]
    public void Run_UsesSharedProcessExecutionRequest_AndReturnsMappedResult()
    {
        var log = new Mock<ILoggingService>();
        var processExecutionService = new Mock<IProcessExecutionService>(MockBehavior.Strict);
        ProcessExecutionRequest? capturedRequest = null;
        processExecutionService
            .Setup(s => s.Run(It.IsAny<ProcessExecutionRequest>()))
            .Callback<ProcessExecutionRequest>(request => capturedRequest = request)
            .Returns(new ProcessExecutionResult(
                Started: true,
                ExitCode: 0,
                TimedOut: false,
                StandardOutput: "output",
                StandardError: string.Empty,
                FailureMessage: null));
        var runner = new AuditPolCommandRunner(log.Object, processExecutionService.Object);

        var result = runner.Run("/get /subcategory:test");

        Assert.NotNull(capturedRequest);
        Assert.Equal("auditpol.exe", capturedRequest!.FileName);
        Assert.Equal("/get /subcategory:test", capturedRequest.Arguments);
        Assert.Equal(TimeSpan.FromSeconds(5), capturedRequest.Timeout);
        Assert.True(capturedRequest.KillEntireProcessTreeOnTimeout);
        Assert.True(capturedRequest.RedirectStandardOutput);
        Assert.True(capturedRequest.RedirectStandardError);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("output", result.StandardOutput);
        Assert.Equal(string.Empty, result.StandardError);
    }

    [Fact]
    public void Run_Timeout_ThrowsTimeoutException()
    {
        var runner = new AuditPolCommandRunner(
            Mock.Of<ILoggingService>(),
            MockProcessExecutionService(new ProcessExecutionResult(
                Started: true,
                ExitCode: null,
                TimedOut: true,
                StandardOutput: string.Empty,
                StandardError: string.Empty,
                FailureMessage: null)));

        var exception = Assert.Throws<TimeoutException>(() => runner.Run("/get /subcategory:test"));

        Assert.Equal("auditpol.exe timed out", exception.Message);
    }

    [Fact]
    public void Run_StartFailure_ThrowsInvalidOperationException()
    {
        var runner = new AuditPolCommandRunner(
            Mock.Of<ILoggingService>(),
            MockProcessExecutionService(new ProcessExecutionResult(
                Started: false,
                ExitCode: null,
                TimedOut: false,
                StandardOutput: string.Empty,
                StandardError: string.Empty,
                FailureMessage: "start failed")));

        var exception = Assert.Throws<InvalidOperationException>(() => runner.Run("/get /subcategory:test"));

        Assert.Equal("start failed", exception.Message);
    }

    private static IProcessExecutionService MockProcessExecutionService(ProcessExecutionResult result)
    {
        var service = new Mock<IProcessExecutionService>(MockBehavior.Strict);
        service.Setup(s => s.Run(It.IsAny<ProcessExecutionRequest>())).Returns(result);
        return service.Object;
    }
}
