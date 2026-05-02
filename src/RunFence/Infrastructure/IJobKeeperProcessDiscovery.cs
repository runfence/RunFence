using System.Security.Principal;

namespace RunFence.Infrastructure;

public interface IJobKeeperProcessDiscovery
{
    int? FindRunningJobKeeperPid(SecurityIdentifier targetUserSid, bool isLow);
}
