using Moq;
using RunFence.Firewall;
using Xunit;

namespace RunFence.Tests;

public class SecurityEventLogBlockedConnectionEventSourceTests
{
    [Fact]
    public void ReadBlockedConnectionEvents_BuildsSecurityQuery_AndParsesOutboundRecords()
    {
        var sinceUtc = new DateTime(2026, 5, 14, 1, 2, 3, DateTimeKind.Utc);
        var recordSource = new Mock<IEventLogRecordSource>(MockBehavior.Strict);
        string? capturedLogName = null;
        string? capturedQuery = null;
        recordSource
            .Setup(s => s.Read(It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string>((logName, query) =>
            {
                capturedLogName = logName;
                capturedQuery = query;
            })
            .Returns([
                new EventLogRecordSnapshot(["", "", "%%14593", "", "", "1.2.3.4", "443", "6"], sinceUtc)
            ]);
        var source = new SecurityEventLogBlockedConnectionEventSource(recordSource.Object);

        var result = source.ReadBlockedConnectionEvents(sinceUtc).ToList();

        var connection = Assert.Single(result);
        Assert.Equal("Security", capturedLogName);
        Assert.Equal(
            "*[System[(EventID=5157) and TimeCreated[@SystemTime>='2026-05-14T01:02:03.000Z']]]",
            capturedQuery);
        Assert.Equal("1.2.3.4", connection.DestAddress);
        Assert.Equal(443, connection.DestPort);
        Assert.Equal(sinceUtc, connection.TimeCreatedUtc);
    }

    [Fact]
    public void ReadBlockedConnectionEvents_SkipsInboundAndMalformedRecords()
    {
        var recordSource = new Mock<IEventLogRecordSource>();
        recordSource
            .Setup(s => s.Read(It.IsAny<string>(), It.IsAny<string>()))
            .Returns([
                new EventLogRecordSnapshot(["", "", "%%14592", "", "", "1.2.3.4", "443", "6"], DateTime.UtcNow),
                new EventLogRecordSnapshot(["", "", "%%14593", "", "", "1.2.3.4", "bad", "6"], DateTime.UtcNow),
                new EventLogRecordSnapshot(["", "", "%%14593", "", "", "", "443", "6"], DateTime.UtcNow),
                new EventLogRecordSnapshot(["", "", "%%14593", "", "", "1.2.3.4", "443", "bad"], DateTime.UtcNow)
            ]);
        var source = new SecurityEventLogBlockedConnectionEventSource(recordSource.Object);

        var result = source.ReadBlockedConnectionEvents(DateTime.UtcNow.AddMinutes(-5));

        Assert.Empty(result);
    }

    [Fact]
    public void ReadBlockedConnectionEvents_SkipsRecordWhenTimeCreatedIsMissing()
    {
        var recordSource = new Mock<IEventLogRecordSource>();
        recordSource
            .Setup(s => s.Read(It.IsAny<string>(), It.IsAny<string>()))
            .Returns([
                new EventLogRecordSnapshot(["", "", "%%14593", "", "", "1.2.3.4", "443", "6"], null)
            ]);
        var source = new SecurityEventLogBlockedConnectionEventSource(recordSource.Object);

        var result = source.ReadBlockedConnectionEvents(DateTime.UtcNow.AddMinutes(-5));

        Assert.Empty(result);
    }
}
