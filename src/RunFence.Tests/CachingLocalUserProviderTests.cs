using Moq;
using RunFence.Acl;
using RunFence.Core;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class CachingLocalUserProviderTests
{
    private readonly ManualClock _clock;
    private readonly CachingLocalUserProvider _localUserProvider;

    public CachingLocalUserProviderTests()
    {
        var log = new Mock<ILoggingService>();
        _clock = new ManualClock(DateTime.UtcNow);
        _localUserProvider = new CachingLocalUserProvider(log.Object, new LocalSamSidResolver(log.Object), _clock);
    }

    [Fact]
    public void GetLocalUserAccounts_ReturnsCachedInstance_WithinTtl()
    {
        var first = _localUserProvider.GetLocalUserAccounts();
        var second = _localUserProvider.GetLocalUserAccounts();

        Assert.Same(first, second);
    }

    [Fact]
    public void GetLocalUserAccounts_AfterTtlAdvance_RefreshesCacheInstance()
    {
        var first = _localUserProvider.GetLocalUserAccounts();

        _clock.UtcNow += TimeSpan.FromSeconds(31);
        var second = _localUserProvider.GetLocalUserAccounts();

        Assert.NotSame(first, second);
    }

    [Fact]
    public void InvalidateCache_CausesRefresh()
    {
        var first = _localUserProvider.GetLocalUserAccounts();

        _localUserProvider.InvalidateCache();
        var second = _localUserProvider.GetLocalUserAccounts();

        Assert.Equal(first, second);
    }

    private sealed class ManualClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; set; } = utcNow;
    }
}
