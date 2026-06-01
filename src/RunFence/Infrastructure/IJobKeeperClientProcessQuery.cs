using System.IO.Pipes;

namespace RunFence.Infrastructure;

public interface IJobKeeperClientProcessQuery
{
    bool TryGetPipeClientProcessId(NamedPipeServerStream pipe, out uint clientPid);
    JobKeeperClientProcessInfo QueryProcessInfo(uint clientPid);
}
