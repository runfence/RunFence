namespace RunFence.Infrastructure;

public interface IProcessIdentitySnapshotReader
{
    ProcessIdentitySnapshot? TryReadProcessIdentity(uint processId);
}
