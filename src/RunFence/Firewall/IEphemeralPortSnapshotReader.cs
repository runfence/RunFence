namespace RunFence.Firewall;

public interface IEphemeralPortSnapshotReader
{
    IReadOnlyList<(int Port, int Pid)> ReadListeningPortsSnapshot(bool isTcp, bool isIPv6);
}
