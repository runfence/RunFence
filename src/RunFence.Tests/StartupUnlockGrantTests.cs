using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class StartupUnlockGrantTests
{
    private long _timestamp;

    [Fact]
    public void TryConsume_ReturnsTrueOnceAfterGrant()
    {
        var grant = CreateGrant();

        grant.Grant();

        Assert.True(grant.TryConsume());
        Assert.False(grant.TryConsume());
    }

    [Fact]
    public void TryConsume_AfterGrantExpires_ReturnsFalseAndClearsGrant()
    {
        var grant = CreateGrant();
        grant.Grant();

        _timestamp = 61;

        Assert.False(grant.TryConsume());
        Assert.False(grant.TryConsume());
    }

    private StartupUnlockGrant CreateGrant() => new(new StubStopwatchProvider(() => _timestamp));

    private sealed class StubStopwatchProvider(Func<long> getTimestamp) : IStopwatchProvider
    {
        public long GetTimestamp() => getTimestamp();

        public double GetElapsedSeconds(long startTimestamp, long endTimestamp) => endTimestamp - startTimestamp;
    }
}
