using Moq;
using RunFence.Core;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class IdleMonitorServiceTests
{
    private readonly Mock<ILoggingService> _log = new();

    private (IdleMonitorService service, Action invokeTimerTick, Action<long> setFakeTick)
        CreateService()
    {
        long fakeTick = 0;
        Action? capturedCallback = null;

        var timeProvider = new StubTimeProvider(() => fakeTick);
        var timerScheduler = new StubTimerScheduler((callback, _) => capturedCallback = callback);

        var service = new IdleMonitorService(
            _log.Object,
            timeProvider: timeProvider,
            timerScheduler: timerScheduler);

        return (
            service,
            invokeTimerTick: () => capturedCallback?.Invoke(),
            setFakeTick: ms => fakeTick = ms);
    }

    private sealed class StubTimeProvider(Func<long> getTick) : ITimeProvider
    {
        public long GetTickCount64() => getTick();
    }

    private sealed class StubTimerScheduler(Action<Action, int> schedule) : ITimerScheduler
    {
        public IDisposable Schedule(Action callback, int intervalMs)
        {
            schedule(callback, intervalMs);
            return NullDisposable.Instance;
        }
    }

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();

        public void Dispose()
        {
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Configure_ZeroOrNegativeTimeout_StartDoesNotLog(int timeoutMinutes)
    {
        var (service, _, _) = CreateService();
        service.Configure(timeoutMinutes);
        service.Start();

        _log.Verify(l => l.Info(It.Is<string>(s => s.Contains("Idle monitor started"))), Times.Never);
    }

    [Fact]
    public void Configure_PositiveTimeout_StartLogsMessage()
    {
        var (service, _, _) = CreateService();
        service.Configure(5);
        service.Start();

        _log.Verify(l => l.Info(It.Is<string>(s => s.Contains("5 minute"))), Times.Once);

        service.Stop();
    }

    [Fact]
    public void Stop_AfterStart_DisposesCleanly()
    {
        var (service, _, _) = CreateService();
        service.Configure(5);
        service.Start();
        service.Stop();

        // Double stop should not throw
        service.Stop();
    }

    [Fact]
    public void Dispose_AfterStart_DisposesCleanly()
    {
        var (service, _, _) = CreateService();
        service.Configure(5);
        service.Start();
        service.Dispose();

        // Double dispose should not throw
        service.Dispose();
    }

    [Fact]
    public void ResetIdleTimer_DoesNotThrow()
    {
        var (service, _, _) = CreateService();
        service.Configure(5);
        service.Start();
        service.ResetIdleTimer();

        service.Stop();
    }

    [Fact]
    public void Start_CalledTwice_StopsFirstTimer()
    {
        var (service, _, _) = CreateService();
        service.Configure(5);
        service.Start();
        service.Start(); // Should stop previous and start new

        _log.Verify(l => l.Info(It.Is<string>(s => s.Contains("Idle monitor started"))), Times.Exactly(2));

        service.Stop();
    }

    [Fact]
    public void IdleTimeoutReached_FiresWhenActivityExceedsTimeout()
    {
        bool eventFired = false;
        var (service, invokeTimerTick, setFakeTick) = CreateService();
        service.IdleTimeoutReached += () => eventFired = true;

        service.Configure(1); // 1 minute = 60000 ms
        setFakeTick(0);
        service.Start(); // records lastActivityTick = 0

        // Advance time past 1 minute threshold
        setFakeTick(120_000);
        invokeTimerTick();

        Assert.True(eventFired);
        _log.Verify(l => l.Info(It.Is<string>(s => s.Contains("Idle timeout reached"))), Times.Once);
    }

    [Fact]
    public void IdleTimeoutReached_DoesNotFireWhenRecentActivity()
    {
        bool eventFired = false;
        var (service, invokeTimerTick, setFakeTick) = CreateService();
        service.IdleTimeoutReached += () => eventFired = true;

        service.Configure(1); // 1 minute = 60000 ms
        setFakeTick(0);
        service.Start();

        // Reset activity at tick 100 — simulates recent form interaction or IPC command
        setFakeTick(100);
        service.ResetIdleTimer(); // lastActivityTick = 100

        // Only 100ms elapsed since reset — well below 60000ms threshold
        setFakeTick(200);
        invokeTimerTick();

        Assert.False(eventFired);
    }

    [Fact]
    public void IdleTimeoutReached_DoesNotFireWhenBelowThreshold()
    {
        bool eventFired = false;
        var (service, invokeTimerTick, setFakeTick) = CreateService();
        service.IdleTimeoutReached += () => eventFired = true;

        service.Configure(5); // 5 minutes = 300000 ms
        setFakeTick(0);
        service.Start(); // lastActivityTick = 0

        // Only 30 seconds elapsed — below 5 minute threshold
        setFakeTick(30_000);
        invokeTimerTick();

        Assert.False(eventFired);
    }
}