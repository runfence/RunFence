using System.IO.Pipes;
using System.Security.Principal;
using RunFence.Core.Models;

namespace RunFence.Infrastructure;

public interface IJobKeeperProcessVerifier
{
    JobKeeperProcessVerificationResult Verify(
        NamedPipeServerStream pipe,
        int expectedPid,
        SecurityIdentifier targetUserSid,
        JobKeeperInstanceIdentity identity);
}
