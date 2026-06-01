namespace RunFence.Infrastructure;

public interface IProcessAppContainerSidReader
{
    string? TryGetProcessAppContainerSid(uint processId);
}
