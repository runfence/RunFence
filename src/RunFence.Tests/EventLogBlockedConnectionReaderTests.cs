using Moq;
using RunFence.Core;
using RunFence.Firewall;
using Xunit;

namespace RunFence.Tests;

public class EventLogBlockedConnectionReaderTests
{
    [Fact]
    public void ReadBlockedConnections_UsesParsedSourceRecords()
    {
        var expectedTime = new DateTime(2026, 5, 14, 12, 0, 0, DateTimeKind.Utc);
        var eventSource = new TestBlockedConnectionEventSource([
            new BlockedConnectionEventRecord("1.2.3.4", 443, expectedTime)
        ]);
        var reader = new EventLogBlockedConnectionReader(
            Mock.Of<ILoggingService>(),
            eventSource,
            Mock.Of<IAuditPolCommandRunner>());

        var result = reader.ReadBlockedConnections(TimeSpan.FromMinutes(5));

        var connection = Assert.Single(result);
        Assert.Equal("1.2.3.4", connection.DestAddress);
        Assert.Equal(443, connection.DestPort);
        Assert.Equal(expectedTime, connection.TimeStamp);
    }

    [Fact]
    public void ReadBlockedConnections_WhenSourceThrows_LogsAndReturnsEmpty()
    {
        var log = new Mock<ILoggingService>();
        var reader = new EventLogBlockedConnectionReader(
            log.Object,
            new ThrowingBlockedConnectionEventSource(new InvalidOperationException("boom")),
            Mock.Of<IAuditPolCommandRunner>());

        var result = reader.ReadBlockedConnections(TimeSpan.FromMinutes(5));

        Assert.Empty(result);
        log.Verify(
            l => l.Error(
                "BlockedConnectionReader: failed to read Security event log",
                It.Is<InvalidOperationException>(ex => ex.Message == "boom")),
            Times.Once);
    }

    [Fact]
    public void ReadBlockedConnections_PassesLookbackBoundaryToReader()
    {
        var before = DateTime.UtcNow - TimeSpan.FromMinutes(10);
        var eventSource = new TestBlockedConnectionEventSource([]);
        var reader = new EventLogBlockedConnectionReader(
            Mock.Of<ILoggingService>(),
            eventSource,
            Mock.Of<IAuditPolCommandRunner>());

        _ = reader.ReadBlockedConnections(TimeSpan.FromMinutes(10));

        Assert.NotNull(eventSource.SinceUtc);
        Assert.InRange((eventSource.SinceUtc.Value - before).Duration(), TimeSpan.Zero, TimeSpan.FromSeconds(2));
    }

    private sealed class TestBlockedConnectionEventSource(List<BlockedConnectionEventRecord> records)
        : IBlockedConnectionEventSource
    {
        public DateTime? SinceUtc { get; private set; }

        public IEnumerable<BlockedConnectionEventRecord> ReadBlockedConnectionEvents(DateTime sinceUtc)
        {
            SinceUtc = sinceUtc;
            return records;
        }
    }

    private sealed class ThrowingBlockedConnectionEventSource(Exception exception)
        : IBlockedConnectionEventSource
    {
        public IEnumerable<BlockedConnectionEventRecord> ReadBlockedConnectionEvents(DateTime sinceUtc)
            => throw exception;
    }
}
