using Moq;
using RunFence.Firewall;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class EphemeralPortOwnershipSnapshotProviderTests
{
    [Fact]
    public void CollectListeningEphemeralPorts_ReusesCachedOwnerWithinTtl()
    {
        var context = new TestContext { CurrentTick = 1000 };
        context.Ports[(true, false)] = [(55000, 42)];
        context.OwnerSids[42] = "S-1-5-21-42";

        var first = context.Provider.CollectListeningEphemeralPorts();
        context.CurrentTick = 2000;
        var second = context.Provider.CollectListeningEphemeralPorts();

        Assert.Equal(1, context.OwnerLookupCount);
        Assert.Single(first[55000].OwnerSids, "S-1-5-21-42");
        Assert.Single(second[55000].OwnerSids, "S-1-5-21-42");
    }

    [Fact]
    public void CollectListeningEphemeralPorts_RefreshesOwnerAfterTtlExpires()
    {
        var context = new TestContext { CurrentTick = 1000 };
        context.Ports[(true, false)] = [(55000, 42)];
        context.OwnerSids[42] = "S-1-5-21-42";

        _ = context.Provider.CollectListeningEphemeralPorts();
        context.CurrentTick = 12_000;
        context.OwnerSids[42] = "S-1-5-21-42-updated";

        var result = context.Provider.CollectListeningEphemeralPorts();

        Assert.Equal(2, context.OwnerLookupCount);
        Assert.Single(result[55000].OwnerSids, "S-1-5-21-42-updated");
    }

    [Fact]
    public void CollectListeningEphemeralPorts_PrunesStalePidCacheEntries()
    {
        var context = new TestContext { CurrentTick = 1000 };
        context.Ports[(true, false)] = [(55000, 42)];
        context.OwnerSids[42] = "S-1-5-21-42";

        _ = context.Provider.CollectListeningEphemeralPorts();

        context.CurrentTick = 2000;
        context.Ports[(true, false)] = [];
        context.Ports[(true, true)] = [(55001, 42)];

        _ = context.Provider.CollectListeningEphemeralPorts();

        context.CurrentTick = 13_000;
        context.Ports[(true, true)] = [];
        context.Ports[(false, false)] = [(55002, 42)];

        _ = context.Provider.CollectListeningEphemeralPorts();

        Assert.Equal(2, context.OwnerLookupCount);
    }

    private sealed class TestContext
    {
        public TestContext()
        {
            var portReader = new Mock<IEphemeralPortSnapshotReader>();
            portReader
                .Setup(r => r.ReadListeningPortsSnapshot(It.IsAny<bool>(), It.IsAny<bool>()))
                .Returns<bool, bool>((isTcp, isIPv6) =>
                    Ports.TryGetValue((isTcp, isIPv6), out var ports) ? ports : []);

            var ownerSidReader = new Mock<IProcessOwnerSidReader>();
            ownerSidReader
                .Setup(r => r.TryGetProcessOwnerSid(It.IsAny<uint>()))
                .Returns<uint>(pid =>
                {
                    OwnerLookupCount++;
                    return OwnerSids[(int)pid];
                });

            var timeProvider = new Mock<ITimeProvider>();
            timeProvider.Setup(t => t.GetTickCount64()).Returns(() => CurrentTick);

            Provider = new EphemeralPortOwnershipSnapshotProvider(
                portReader.Object,
                ownerSidReader.Object,
                timeProvider.Object);
        }

        public EphemeralPortOwnershipSnapshotProvider Provider { get; }
        public long CurrentTick { get; set; }
        public int OwnerLookupCount { get; set; }
        public Dictionary<(bool IsTcp, bool IsIpv6), IReadOnlyList<(int Port, int Pid)>> Ports { get; } = new();
        public Dictionary<int, string?> OwnerSids { get; } = new();
    }
}
