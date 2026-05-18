namespace RunFence.Firewall;

public interface IBlockedConnectionEventSource
{
    IEnumerable<BlockedConnectionEventRecord> ReadBlockedConnectionEvents(DateTime sinceUtc);
}
