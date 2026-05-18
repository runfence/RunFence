using RunFence.Infrastructure;

namespace RunFence.Firewall;

public class EphemeralPortOwnershipSnapshotProvider(
    IEphemeralPortSnapshotReader portSnapshotReader,
    IProcessOwnerSidReader processOwnerSidReader,
    ITimeProvider timeProvider)
    : IEphemeralPortOwnershipSnapshotProvider
{
    private const int EphemeralPortRangeStart = 49152;

    private readonly Dictionary<int, (string? Sid, long Timestamp)> _pidSidCache = new();
    private readonly Lock _lock = new();

    public Dictionary<int, PortOwnerSet> CollectListeningEphemeralPorts()
    {
        var result = new Dictionary<int, PortOwnerSet>();
        var allPorts = new List<(int Port, int Pid)>();
        allPorts.AddRange(portSnapshotReader.ReadListeningPortsSnapshot(isTcp: true, isIPv6: false));
        allPorts.AddRange(portSnapshotReader.ReadListeningPortsSnapshot(isTcp: true, isIPv6: true));
        allPorts.AddRange(portSnapshotReader.ReadListeningPortsSnapshot(isTcp: false, isIPv6: false));
        allPorts.AddRange(portSnapshotReader.ReadListeningPortsSnapshot(isTcp: false, isIPv6: true));

        var observedPids = new HashSet<int>();

        foreach (var (port, pid) in allPorts)
        {
            if (port < EphemeralPortRangeStart)
                continue;

            observedPids.Add(pid);

            var now = timeProvider.GetTickCount64();
            string? ownerSid;
            var fresh = false;
            lock (_lock)
            {
                if (_pidSidCache.TryGetValue(pid, out var cached) && now - cached.Timestamp < 10_000)
                {
                    ownerSid = cached.Sid;
                    _pidSidCache[pid] = (cached.Sid, now);
                    fresh = true;
                }
                else
                {
                    ownerSid = null;
                }
            }

            if (!fresh)
            {
                ownerSid = processOwnerSidReader.TryGetProcessOwnerSid((uint)pid);
                lock (_lock)
                {
                    _pidSidCache[pid] = (ownerSid, now);
                }
            }

            if (!result.TryGetValue(port, out var ownerSet))
            {
                ownerSet = new PortOwnerSet();
                result[port] = ownerSet;
            }

            ownerSet.AddOwner(ownerSid);
        }

        lock (_lock)
        {
            var stale = _pidSidCache.Keys.Where(pid => !observedPids.Contains(pid)).ToList();
            foreach (var pid in stale)
                _pidSidCache.Remove(pid);
        }

        return result;
    }

}
