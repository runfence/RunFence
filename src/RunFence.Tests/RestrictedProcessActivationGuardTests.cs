using Moq;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Launch.Tokens;
using Xunit;

namespace RunFence.Tests;

public class RestrictedProcessActivationGuardTests
{
    private const string Sid = "S-1-5-21-100-200-300-1001";
    private static readonly IntPtr ProcessHandle = new(10);
    private static readonly IntPtr ThreadHandle = new(20);

    private readonly Mock<IProcessControl> _processControl = new();
    private readonly RestrictedProcessActivationGuard _guard;

    public RestrictedProcessActivationGuardTests()
    {
        _guard = new RestrictedProcessActivationGuard(_processControl.Object);
    }

    [Theory]
    [InlineData("job keeper")]
    [InlineData("direct fallback target")]
    public void ThrowIfAssignmentFailed_TerminatesAndClosesWithoutResume(string role)
    {
        var processInfo = ProcessInfo();
        var assignment = JobAssignmentResult.Failure(
            JobAssignment.Restricted,
            "job",
            1234,
            $"{role} assignment failed",
            JobAssignmentFailureKind.AssignProcessFailed);

        Assert.Throws<LaunchFailedException>(() =>
            _guard.ThrowIfAssignmentFailed(ref processInfo, assignment, Sid, isLow: false));

        _processControl.Verify(c => c.ResumeThread(It.IsAny<IntPtr>(), out It.Ref<int>.IsAny), Times.Never);
        _processControl.Verify(c => c.TerminateProcessBestEffort(ProcessHandle, 1), Times.Once);
        _processControl.Verify(c => c.CloseHandle(ThreadHandle), Times.Once);
        _processControl.Verify(c => c.CloseHandle(ProcessHandle), Times.Once);
        Assert.Equal(IntPtr.Zero, processInfo.hThread);
        Assert.Equal(IntPtr.Zero, processInfo.hProcess);
    }

    [Fact]
    public void ResumeOrTerminate_MissingThread_TerminatesBeforeThrowing()
    {
        var processInfo = ProcessInfo() with { hThread = IntPtr.Zero };

        Assert.Throws<LaunchFailedException>(() =>
            _guard.ResumeOrTerminate(ref processInfo, Sid, isLow: false, "job keeper"));

        _processControl.Verify(c => c.ResumeThread(It.IsAny<IntPtr>(), out It.Ref<int>.IsAny), Times.Never);
        _processControl.Verify(c => c.TerminateProcessBestEffort(ProcessHandle, 1), Times.Once);
        _processControl.Verify(c => c.CloseHandle(ProcessHandle), Times.Once);
        Assert.Equal(IntPtr.Zero, processInfo.hProcess);
    }

    [Fact]
    public void ResumeOrTerminate_ResumeFails_TerminatesAndCloses()
    {
        var processInfo = ProcessInfo();
        var error = 5;
        _processControl
            .Setup(c => c.ResumeThread(ThreadHandle, out error))
            .Returns(false);

        Assert.Throws<LaunchFailedException>(() =>
            _guard.ResumeOrTerminate(ref processInfo, Sid, isLow: true, "direct fallback target"));

        _processControl.Verify(c => c.TerminateProcessBestEffort(ProcessHandle, 1), Times.Once);
        _processControl.Verify(c => c.CloseHandle(ThreadHandle), Times.Once);
        _processControl.Verify(c => c.CloseHandle(ProcessHandle), Times.Once);
        Assert.Equal(IntPtr.Zero, processInfo.hThread);
        Assert.Equal(IntPtr.Zero, processInfo.hProcess);
    }

    private static ProcessLaunchNative.PROCESS_INFORMATION ProcessInfo() => new()
    {
        hProcess = ProcessHandle,
        hThread = ThreadHandle,
        dwProcessId = 1234,
        dwThreadId = 5678,
    };
}
