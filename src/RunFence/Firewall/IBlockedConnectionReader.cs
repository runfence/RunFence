namespace RunFence.Firewall;

public interface IBlockedConnectionReader
{
    List<BlockedConnection> ReadBlockedConnections(TimeSpan lookback);
    bool IsAuditPolicyEnabled();
    void SetAuditPolicyEnabled(bool enabled);
}