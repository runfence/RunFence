namespace RunFence.Firewall;

public class SecurityEventLogBlockedConnectionEventSource(IEventLogRecordSource eventLogRecordSource)
    : IBlockedConnectionEventSource
{
    private const string LogName = "Security";
    private const int EventId5157 = 5157;
    private const int PropDirection = 2;
    private const int PropDestAddress = 5;
    private const int PropDestPort = 6;
    private const int PropProtocol = 7;
    private const string OutboundDirection = "%%14593";

    public IEnumerable<BlockedConnectionEventRecord> ReadBlockedConnectionEvents(DateTime sinceUtc)
    {
        foreach (var record in eventLogRecordSource.Read(LogName, BuildBlockedConnectionQuery(sinceUtc)))
        {
            if (TryParse(record, out var blockedConnection))
                yield return blockedConnection;
        }
    }

    private static string BuildBlockedConnectionQuery(DateTime sinceUtc) =>
        $"*[System[(EventID={EventId5157}) and TimeCreated[@SystemTime>='{sinceUtc:yyyy-MM-ddTHH:mm:ss.000Z}']]]";

    private static bool TryParse(EventLogRecordSnapshot record, out BlockedConnectionEventRecord blockedConnection)
    {
        blockedConnection = null!;

        if (record.Properties.Count <= PropProtocol)
            return false;

        var direction = record.Properties[PropDirection]?.ToString();
        if (!string.Equals(direction, OutboundDirection, StringComparison.Ordinal))
            return false;

        var destAddress = record.Properties[PropDestAddress]?.ToString();
        if (string.IsNullOrWhiteSpace(destAddress))
            return false;

        if (!int.TryParse(record.Properties[PropDestPort]?.ToString(), out var destPort))
            return false;

        if (!int.TryParse(record.Properties[PropProtocol]?.ToString(), out _))
            return false;

        if (record.TimeCreated == null)
            return false;

        blockedConnection = new BlockedConnectionEventRecord(
            destAddress,
            destPort,
            record.TimeCreated.Value.ToUniversalTime());
        return true;
    }
}
