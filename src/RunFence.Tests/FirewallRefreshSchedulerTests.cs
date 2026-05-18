using Moq;
using RunFence.Core;
using RunFence.Firewall;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class FirewallRefreshSchedulerTests
{
    private readonly Mock<ILoggingService> _log = new();

    [Fact]
    [Trait("Category", "TimingSensitive")]
    public async Task RequestRefresh_MultipleRequestsWhileCycleRuns_CoalescesIntoSingleFollowUpCycle()
    {
        var firstCycleEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstCycle = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int runCount = 0;
        using var scheduler = new SchedulerHarness(_log.Object, async () =>
        {
            int count = Interlocked.Increment(ref runCount);
            if (count == 1)
            {
                firstCycleEntered.TrySetResult(true);
                await releaseFirstCycle.Task;
            }

            await Task.CompletedTask;
        });

        scheduler.Instance.Start();
        scheduler.Instance.RequestRefresh();
        await firstCycleEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));

        scheduler.Instance.RequestRefresh();
        scheduler.Instance.RequestRefresh();
        scheduler.Instance.RequestRefresh();
        releaseFirstCycle.TrySetResult(true);
        await scheduler.Instance.DrainAsync();

        Assert.Equal(2, Volatile.Read(ref runCount));
    }

    [Fact]
    [Trait("Category", "TimingSensitive")]
    public async Task DrainAsync_WaitsForActiveCycleToComplete()
    {
        var cycleEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCycle = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var scheduler = new SchedulerHarness(_log.Object, async () =>
        {
            cycleEntered.TrySetResult(true);
            await releaseCycle.Task;
        });

        scheduler.Instance.Start();
        scheduler.Instance.RequestRefresh();
        await cycleEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Task drainTask = scheduler.Instance.DrainAsync();
        Assert.False(drainTask.IsCompleted);
        releaseCycle.TrySetResult(true);
        await drainTask;
    }

    [Fact]
    [Trait("Category", "TimingSensitive")]
    public async Task RequestRefresh_WhenCycleThrows_WorkerRecoversForNextRequest()
    {
        bool throwOnFirstRun = true;
        int runCount = 0;
        using var scheduler = new SchedulerHarness(_log.Object, () =>
        {
            Interlocked.Increment(ref runCount);
            if (throwOnFirstRun)
            {
                throwOnFirstRun = false;
                throw new InvalidOperationException("refresh failed");
            }

            return Task.CompletedTask;
        });

        scheduler.Instance.Start();
        scheduler.Instance.RequestRefresh();
        await scheduler.Instance.DrainAsync();

        scheduler.Instance.RequestRefresh();
        await scheduler.Instance.DrainAsync();

        Assert.Equal(2, Volatile.Read(ref runCount));
        _log.Verify(l => l.Error(
            It.Is<string>(message => message.Contains("DNS refresh cycle failed", StringComparison.Ordinal)),
            It.IsAny<Exception>()), Times.Once);
    }

    private sealed class SchedulerHarness : IDisposable
    {
        public FirewallRefreshScheduler Instance { get; }

        public SchedulerHarness(ILoggingService log, Func<Task> runCycleAsync)
        {
            Instance = new FirewallRefreshScheduler(
                new NoOpTimerScheduler(),
                log,
                runCycleAsync);
        }

        public void Dispose()
        {
            Instance.Stop();
        }
    }

    private sealed class NoOpTimerScheduler : ITimerScheduler
    {
        public IDisposable Schedule(Action callback, int intervalMs) => new NoOpDisposable();
    }

    private sealed class NoOpDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
