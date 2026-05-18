namespace RunFence.Infrastructure;

public interface IJobKeeperRegistry
{
    bool Has(string sid, bool isLow);
    void Register(string sid, bool isLow, JobKeeperState state);
    bool TryGet(string sid, bool isLow, out JobKeeperState state);
    void RemoveAndDispose(string sid, bool isLow, JobKeeperState? expectedState = null);
}
