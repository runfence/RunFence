using Moq;
using RunFence.Core;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class IdleMonitorServiceTests
{
    private readonly Mock<ILoggingService> _log = new();

    private record struct IdleMonitorTestContext(
        IdleMonitorService Service, Action InvokeTimerTick, Action<long> SetFakeTick);

    private IdleMonitorTestContext CreateService()
    {
        long fakeTick = 0;
        Action? capturedCallback = null;

        var timeProvider = new StubTimeProvider(() => fakeTick);
        var timerScheduler = new StubTimerScheduler((callback, _) => capturedCallback = callback);

        var service = new IdleMonitorService(
            _log.Object,
            timeProvider: timeProvider,
            timerScheduler: timerScheduler);

        return new IdleMonitorTestContext(
            service,
            InvokeTimerTick: () => capturedCallback?.Invoke(),
            SetFakeTick: ms => fakeTick = ms);
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

    // ── BHV-21: Do NOT reschedule after IdleTimeoutReached fires ─────────────

    [Fact]
    public void IdleTimeoutReached_DoesNotRescheduleAfterTimeoutFires()
    {
        // Arrange
        int scheduleCallCount = 0;
        Action? capturedCallback = null;
        long fakeTick = 0;

        var timeProvider = new StubTimeProvider(() => fakeTick);
        var timerScheduler = new StubTimerScheduler((callback, _) =>
        {
            capturedCallback = callback;
            scheduleCallCount++;
        });

        var service = new IdleMonitorService(_log.Object, timeProvider, timerScheduler);
        bool eventFired = false;
        service.IdleTimeoutReached += () => eventFired = true;

        service.Configure(1); // 1 minute
        fakeTick = 0;
        service.Start();
        var countAfterStart = scheduleCallCount;

        // Act: advance past timeout and trigger callback
        fakeTick = 120_000;
        capturedCallback?.Invoke();

        // Assert: event fired but timer was NOT rescheduled (fires exactly once)
        Assert.True(eventFired);
        Assert.Equal(countAfterStart, scheduleCallCount);

        service.Stop();
    }

    [Fact]
    public void IdleTimeoutReached_ReschedulesAfterNonTimeoutTick()
    {
        // Arrange
        int scheduleCallCount = 0;
        Action? capturedCallback = null;
        long fakeTick = 0;

        var timeProvider = new StubTimeProvider(() => fakeTick);
        var timerScheduler = new StubTimerScheduler((callback, _) =>
        {
            capturedCallback = callback;
            scheduleCallCount++;
        });

        var service = new IdleMonitorService(_log.Object, timeProvider, timerScheduler);
        service.Configure(5); // 5 minutes
        fakeTick = 0;
        service.Start();
        var countAfterStart = scheduleCallCount;

        // Act: tick without reaching timeout
        fakeTick = 10_000;
        capturedCallback?.Invoke();

        // Assert: timer was rescheduled even when not timed out
        Assert.True(scheduleCallCount > countAfterStart);

        service.Stop();
    }
}
