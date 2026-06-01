namespace RunFence.Infrastructure;

public interface IProcessImagePathReader
{
    string? TryGetProcessImagePath(uint processId);
}
