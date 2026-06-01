using System.Diagnostics;
using Moq;
using RunFence.Core;
using RunFence.Launch.Tokens;
using RunFence.PrefTrans;
using Xunit;

namespace RunFence.Tests;

public class PrefTransProcessWaiterTests
{
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<IPrefTransLogWorkspace> _workspace = new(MockBehavior.Strict);
    private readonly Mock<IPrefTransProcessHandleFactory> _processHandleFactory = new(MockBehavior.Strict);
    private readonly Mock<IPrefTransTimeoutClockFactory> _timeoutClockFactory = new(MockBehavior.Strict);

    [Fact]
    public void WaitForResult_ExitCodeZero_ReturnsSuccess()
    {
        SetupProcessHandle(0, enqueueExitResults: [true]);
        _workspace.Setup(w => w.ReadLogFile(It.IsAny<string>())).Throws(new Xunit.Sdk.XunitException("ReadLogFile should not be called."));
        var waiter = CreateWaiter();

        var result = waiter.WaitForResult(TestProcessInfoFactory.Empty(), 10_000, "unused.log", null);

        Assert.True(result.Success);
        Assert.Equal(string.Empty, result.Message);
        _workspace.Verify(w => w.ReadLogFile(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void WaitForResult_NonZeroExitCode_WithLogContent_ReturnsLogContent()
    {
        SetupProcessHandle(7, enqueueExitResults: [true]);
        _workspace.Setup(w => w.ReadLogFile("failure.log")).Returns("helper failed");
        var waiter = CreateWaiter();

        var result = waiter.WaitForResult(TestProcessInfoFactory.Empty(), 10_000, "failure.log", null);

        Assert.False(result.Success);
        Assert.Equal("helper failed", result.Message);
        _workspace.Verify(w => w.ReadLogFile("failure.log"), Times.Once);
    }

    [Fact]
    public void WaitForResult_NonZeroExitCode_WithoutLogContent_ReturnsExitCodeMessage()
    {
        SetupProcessHandle(9, enqueueExitResults: [true]);
        _workspace.Setup(w => w.ReadLogFile("failure.log")).Returns(string.Empty);
        var waiter = CreateWaiter();

        var result = waiter.WaitForResult(TestProcessInfoFactory.Empty(), 10_000, "failure.log", null);

        Assert.False(result.Success);
        Assert.Equal("Error code: 9", result.Message);
        _workspace.Verify(w => w.ReadLogFile("failure.log"), Times.Once);
    }

    [Fact]
    public void WaitForResult_Timeout_KillsProcessAndReturnsTimeout()
    {
        _workspace.Setup(w => w.ReadLogFile(It.IsAny<string>())).Throws(new Xunit.Sdk.XunitException("ReadLogFile should not be called on timeout."));
        var timeoutClock = new FakeTimeoutClock();
        var processHandle = new FakeProcessHandle(timeoutClock)
        {
            ExitCode = 0
        };
        processHandle.WaitForExitResults.Enqueue(false);
        processHandle.WaitForExitResults.Enqueue(false);
        processHandle.WaitForExitResults.Enqueue(true);
        _processHandleFactory
            .Setup(factory => factory.Create(It.IsAny<ProcessInfo>()))
            .Returns(processHandle);
        _timeoutClockFactory
            .Setup(factory => factory.StartNew())
            .Returns(timeoutClock);
        var waiter = CreateWaiter();
        var pollCount = 0;

        var result = waiter.WaitForResult(
            TestProcessInfoFactory.Empty(),
            700,
            "unused.log",
            () => pollCount++);

        Assert.False(result.Success);
        Assert.Equal("Operation timed out", result.Message);
        Assert.Equal(1, pollCount);
        Assert.Equal([500, 500, 2000], processHandle.WaitForExitCalls);
        Assert.Equal([1], processHandle.KillCalls);
        Assert.True(processHandle.DisposeCalled);
        _workspace.Verify(w => w.ReadLogFile(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void WaitForResult_Timeout_DoesNotInvokePollCallbackAfterTimeoutDecision()
    {
        _workspace.Setup(w => w.ReadLogFile(It.IsAny<string>())).Throws(new Xunit.Sdk.XunitException("ReadLogFile should not be called on timeout."));
        var timeoutClock = new FakeTimeoutClock();
        var processHandle = new FakeProcessHandle(timeoutClock)
        {
            ExitCode = 0
        };
        processHandle.WaitForExitResults.Enqueue(false);
        _processHandleFactory
            .Setup(factory => factory.Create(It.IsAny<ProcessInfo>()))
            .Returns(processHandle);
        _timeoutClockFactory
            .Setup(factory => factory.StartNew())
            .Returns(timeoutClock);
        var waiter = CreateWaiter();
        timeoutClock.ElapsedMillisecondsValue = 701;
        var pollCount = 0;

        var result = waiter.WaitForResult(
            TestProcessInfoFactory.Empty(),
            700,
            "unused.log",
            () => pollCount++);

        Assert.False(result.Success);
        Assert.Equal(0, pollCount);
        Assert.Equal([500, 2000], processHandle.WaitForExitCalls);
        Assert.Equal([1], processHandle.KillCalls);
    }

    [Fact]
    public void WaitForResult_TimesOutWhenElapsedMillisecondsMatchesTimeoutExactly()
    {
        _workspace.Setup(w => w.ReadLogFile(It.IsAny<string>())).Throws(new Xunit.Sdk.XunitException("ReadLogFile should not be called on timeout."));
        var timeoutClock = new FakeTimeoutClock();
        var processHandle = new FakeProcessHandle(timeoutClock)
        {
            ExitCode = 0
        };
        processHandle.WaitForExitResults.Enqueue(false);
        _processHandleFactory
            .Setup(factory => factory.Create(It.IsAny<ProcessInfo>()))
            .Returns(processHandle);
        _timeoutClockFactory
            .Setup(factory => factory.StartNew())
            .Returns(timeoutClock);
        var waiter = CreateWaiter();
        timeoutClock.ElapsedMillisecondsValue = 700;

        var result = waiter.WaitForResult(
            TestProcessInfoFactory.Empty(),
            700,
            "unused.log",
            null);

        Assert.False(result.Success);
        Assert.Equal("Operation timed out", result.Message);
        Assert.Equal([500, 2000], processHandle.WaitForExitCalls);
        Assert.Equal([1], processHandle.KillCalls);
    }

    private PrefTransProcessWaiter CreateWaiter()
        => new(
            _log.Object,
            _workspace.Object,
            _processHandleFactory.Object,
            _timeoutClockFactory.Object);

    private sealed class FakeProcessHandle(FakeTimeoutClock timeoutClock) : IPrefTransProcessHandle
    {
        public Queue<bool> WaitForExitResults { get; } = [];
        public List<int> WaitForExitCalls { get; } = [];
        public List<int> KillCalls { get; } = [];
        public bool DisposeCalled { get; private set; }
        public int ExitCode { get; set; }

        public bool WaitForExit(int milliseconds)
        {
            WaitForExitCalls.Add(milliseconds);
            if (milliseconds == 500)
                timeoutClock.ElapsedMillisecondsValue += milliseconds;

            return WaitForExitResults.Count > 0 && WaitForExitResults.Dequeue();
        }

        public void Kill(int gracePeriodSeconds) => KillCalls.Add(gracePeriodSeconds);

        public void Dispose() => DisposeCalled = true;
    }

    private sealed class FakeTimeoutClock : IPrefTransTimeoutClock
    {
        public long ElapsedMillisecondsValue { get; set; }

        public long ElapsedMilliseconds => ElapsedMillisecondsValue;
    }

    private void SetupProcessHandle(int exitCode, params bool[]? enqueueExitResults)
    {
        var timeoutClock = new FakeTimeoutClock();
        var processHandle = new FakeProcessHandle(timeoutClock)
        {
            ExitCode = exitCode
        };

        if (enqueueExitResults is not null)
        {
            foreach (var result in enqueueExitResults)
                processHandle.WaitForExitResults.Enqueue(result);
        }

        _processHandleFactory
            .Setup(factory => factory.Create(It.IsAny<ProcessInfo>()))
            .Returns(processHandle);
        _timeoutClockFactory
            .Setup(factory => factory.StartNew())
            .Returns(timeoutClock);

    }

    public PrefTransProcessWaiterTests()
    {
        _timeoutClockFactory
            .Setup(factory => factory.StartNew())
            .Returns(() => new PrefTransStopwatchTimeoutClock(Stopwatch.StartNew()));
    }
}
