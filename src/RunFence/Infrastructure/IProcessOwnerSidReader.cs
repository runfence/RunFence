namespace RunFence.Infrastructure;

public interface IProcessOwnerSidReader
{
    string? TryGetProcessOwnerSid(uint processId);
}
