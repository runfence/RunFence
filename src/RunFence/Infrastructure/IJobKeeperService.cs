using System.IO.Pipes;
using System.Security.Principal;
using RunFence.Core.Models;

namespace RunFence.Infrastructure;

public interface IJobKeeperService
{
    /// <summary>True if a live (pipe-connected) job keeper exists for this SID and IL.</summary>
    bool HasJobKeeper(string sid, bool isLow);

    /// <summary>
    /// Waits (up to 10 s) for a newly launched job keeper to connect to <paramref name="pipeServer"/>,
    /// verifies its PID, image path, owner SID, integrity level, and named-job membership/security,
    /// then registers the connection. Disposes the pipe on failure.
    /// Returns the verified PID on success, 0 on failure.
    /// </summary>
    int WaitAndRegisterJobKeeper(
        JobKeeperInstanceIdentity identity,
        NamedPipeServerStream pipeServer,
        int expectedPid,
        SecurityIdentifier targetUserSid);

    /// <summary>
    /// Scans running processes for an existing job keeper (e.g. from a previous RunFence session)
    /// matching the target SID and IL. When found, creates a pipe server and waits for the keeper
    /// to reconnect, then registers it. Returns the verified PID or 0 if no keeper is found or
    /// the persisted identity is stale/unverifiable; stale identities are removed for that SID/mode.
    /// </summary>
    int TryReconnectExistingJobKeeper(string sid, bool isLow, SecurityIdentifier targetUserSid);

    /// <summary>Removes the cached keeper state (pipe is already broken or process is dead).</summary>
    void RemoveJobKeeper(string sid, bool isLow);
}
