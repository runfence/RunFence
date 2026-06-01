using RunFence.Core.Models;

namespace RunFence.Infrastructure;

public interface IJobKeeperIdentityStore
{
    JobKeeperInstanceIdentity? Get(string sid, bool isLow);
    IReadOnlyList<JobKeeperInstanceIdentity> GetAll();
    JobKeeperInstanceIdentity CreateFresh(string sid, bool isLow);
    void Remove(string sid, bool isLow);
    void UpdateLastVerifiedPid(JobKeeperInstanceIdentity identity, int keeperPid);
}
