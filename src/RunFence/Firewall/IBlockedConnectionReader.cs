namespace RunFence.Firewall;

public interface IBlockedConnectionReader
{
    List<BlockedConnection> ReadBlockedConnections(TimeSpan lookback);
}
