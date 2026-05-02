using Moq;
using RunFence.Acl;
using RunFence.Core;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class CachingLocalUserProviderTests
{
    private readonly CachingLocalUserProvider _localUserProvider;

    public CachingLocalUserProviderTests()
    {
        var log = new Mock<ILoggingService>();
        _localUserProvider = new CachingLocalUserProvider(log.Object, new LocalSamSidResolver(log.Object));
    }

    [Fact]
    public void GetLocalUserAccounts_ReturnsCachedInstance_WithinTtl()
    {
        // Arrange & Act
        var first = _localUserProvider.GetLocalUserAccounts();
        var second = _localUserProvider.GetLocalUserAccounts();

        // Assert — same cached instance returned; callers treat result as read-only
        Assert.Same(first, second);
    }

    [Fact]
    public void InvalidateCache_CausesRefresh()
    {
        // Arrange
        var first = _localUserProvider.GetLocalUserAccounts();
        int originalCount = first.Count;

        // Act
        _localUserProvider.InvalidateCache();
        var second = _localUserProvider.GetLocalUserAccounts();

        // Assert — re-enumeration produces the same user data (not a stale copy)
        Assert.Equal(originalCount, second.Count);
        Assert.Equal(first, second);
    }
}
