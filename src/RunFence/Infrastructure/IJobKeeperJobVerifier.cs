using RunFence.Core.Models;

namespace RunFence.Infrastructure;

public interface IJobKeeperJobVerifier
{
    JobKeeperJobVerificationResult Verify(JobKeeperInstanceIdentity identity, int keeperPid);
}
