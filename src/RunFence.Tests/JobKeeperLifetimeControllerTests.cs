using RunFence.JobKeeper;
using Xunit;

namespace RunFence.Tests;

public sealed class JobKeeperLifetimeControllerTests
{
    [Fact]
    public void ShouldExit_BeforeIdleTimeout_ReturnsFalse()
    {
        var clock = new FakeClock(0);
        var registry = new FakeChildProcessRegistry([0], tryExitAfterCleanupResult: true);
        var controller = new JobKeeperLifetimeController(clock, registry);

        clock.Advance(JobKeeperLifetimeController.IdleTimeoutMilliseconds - 1);

        Assert.False(controller.ShouldExit());
    }

    [Fact]
    public void ShouldExit_AfterThreeZeroChildSamplesAndIdleTimeout_ReturnsTrue()
    {
        var clock = new FakeClock(0);
        var registry = new FakeChildProcessRegistry([0], tryExitAfterCleanupResult: true);
        var controller = new JobKeeperLifetimeController(clock, registry);

        clock.Advance(JobKeeperLifetimeController.ChildSampleIntervalMilliseconds);
        Assert.False(controller.ShouldExit());

        clock.Advance(JobKeeperLifetimeController.ChildSampleIntervalMilliseconds);
        Assert.False(controller.ShouldExit());

        clock.Advance(JobKeeperLifetimeController.ChildSampleIntervalMilliseconds);

        Assert.True(controller.ShouldExit());
        Assert.Equal(1, registry.TryExitAfterCleaningIgnoredProcessesCallCount);
    }

    [Fact]
    public void ShouldExit_RequiresConsecutiveZeroChildSamples()
    {
        var clock = new FakeClock(0);
        var registry = new FakeChildProcessRegistry([0, 2, 0, 0, 0], tryExitAfterCleanupResult: true);
        var controller = new JobKeeperLifetimeController(clock, registry);

        clock.Advance(JobKeeperLifetimeController.ChildSampleIntervalMilliseconds);
        Assert.False(controller.ShouldExit());

        clock.Advance(JobKeeperLifetimeController.ChildSampleIntervalMilliseconds);
        Assert.False(controller.ShouldExit());

        clock.Advance(JobKeeperLifetimeController.ChildSampleIntervalMilliseconds);
        Assert.False(controller.ShouldExit());

        clock.Advance(JobKeeperLifetimeController.ChildSampleIntervalMilliseconds);
        Assert.False(controller.ShouldExit());

        clock.Advance(JobKeeperLifetimeController.ChildSampleIntervalMilliseconds);
        Assert.True(controller.ShouldExit());
    }

    [Fact]
    public void RecordRequestArrival_ExtendsIdleWindow()
    {
        var clock = new FakeClock(0);
        var registry = new FakeChildProcessRegistry([0], tryExitAfterCleanupResult: true);
        var controller = new JobKeeperLifetimeController(clock, registry);

        clock.Advance(JobKeeperLifetimeController.ChildSampleIntervalMilliseconds);
        Assert.False(controller.ShouldExit());

        clock.Advance(JobKeeperLifetimeController.ChildSampleIntervalMilliseconds);
        Assert.False(controller.ShouldExit());

        clock.Advance(JobKeeperLifetimeController.ChildSampleIntervalMilliseconds);
        controller.RecordRequestArrival();
        Assert.False(controller.ShouldExit());

        clock.Advance(JobKeeperLifetimeController.ChildSampleIntervalMilliseconds);
        Assert.False(controller.ShouldExit());

        clock.Advance(JobKeeperLifetimeController.ChildSampleIntervalMilliseconds);
        Assert.False(controller.ShouldExit());

        clock.Advance(JobKeeperLifetimeController.ChildSampleIntervalMilliseconds);
        Assert.True(controller.ShouldExit());
    }

    [Fact]
    public void ShouldExit_WhenCleanupRecheckFindsNewProcess_ReturnsFalse()
    {
        var clock = new FakeClock(0);
        var registry = new FakeChildProcessRegistry([0], tryExitAfterCleanupResult: false);
        var controller = new JobKeeperLifetimeController(clock, registry);

        clock.Advance(JobKeeperLifetimeController.ChildSampleIntervalMilliseconds);
        Assert.False(controller.ShouldExit());

        clock.Advance(JobKeeperLifetimeController.ChildSampleIntervalMilliseconds);
        Assert.False(controller.ShouldExit());

        clock.Advance(JobKeeperLifetimeController.ChildSampleIntervalMilliseconds);

        Assert.False(controller.ShouldExit());
        Assert.Equal(1, registry.TryExitAfterCleaningIgnoredProcessesCallCount);
    }

    private sealed class FakeClock(long milliseconds) : IJobKeeperClock
    {
        private long _milliseconds = milliseconds;

        public long GetMilliseconds() => _milliseconds;

        public void Advance(long delta) => _milliseconds += delta;
    }

    private sealed class FakeChildProcessRegistry(int[] activeChildren, bool tryExitAfterCleanupResult) : IJobKeeperChildProcessRegistry
    {
        private readonly Queue<int> _activeChildren = new(activeChildren.Length == 0 ? [0] : activeChildren);

        public int TryExitAfterCleaningIgnoredProcessesCallCount { get; private set; }

        public void Register(IntPtr processHandle)
        {
        }

        public int PruneExitedAndCountActive()
        {
            if (_activeChildren.Count == 1)
                return _activeChildren.Peek();

            return _activeChildren.Dequeue();
        }

        public bool TryExitAfterCleaningIgnoredProcesses()
        {
            TryExitAfterCleaningIgnoredProcessesCallCount++;
            return tryExitAfterCleanupResult;
        }
    }
}
