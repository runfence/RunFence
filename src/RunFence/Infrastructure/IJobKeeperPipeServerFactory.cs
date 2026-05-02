using System.IO.Pipes;
using System.Security.Principal;
using RunFence.Core.Models;

namespace RunFence.Infrastructure;

public interface IJobKeeperPipeServerFactory
{
    NamedPipeServerStream Create(JobKeeperInstanceIdentity identity, SecurityIdentifier targetUserSid);
}
