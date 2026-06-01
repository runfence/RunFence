using System.IO.Pipes;
using System.Security.Principal;

namespace RunFence.Infrastructure;

public sealed class JobKeeperClientProcessQuery(IProcessIdentitySnapshotReader processIdentitySnapshotReader) : IJobKeeperClientProcessQuery
{
    public bool TryGetPipeClientProcessId(NamedPipeServerStream pipe, out uint clientPid)
    {
        ArgumentNullException.ThrowIfNull(pipe);
        return ProcessNative.GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out clientPid);
    }

    public JobKeeperClientProcessInfo QueryProcessInfo(uint clientPid)
    {
        var snapshot = processIdentitySnapshotReader.TryReadProcessIdentity(clientPid);
        if (!snapshot.HasValue)
            return default;

        SecurityIdentifier? ownerSid = null;
        if (!string.IsNullOrWhiteSpace(snapshot.Value.OwnerSid))
        {
            try
            {
                ownerSid = new SecurityIdentifier(snapshot.Value.OwnerSid);
            }
            catch (ArgumentException)
            {
                ownerSid = null;
            }
        }

        return new JobKeeperClientProcessInfo(
            snapshot.Value.ImagePath,
            ownerSid,
            snapshot.Value.IntegrityLevel);
    }
}
