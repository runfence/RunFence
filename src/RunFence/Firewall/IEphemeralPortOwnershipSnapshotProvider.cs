namespace RunFence.Firewall;

public interface IEphemeralPortOwnershipSnapshotProvider
{
    Dictionary<int, PortOwnerSet> CollectListeningEphemeralPorts();
}
