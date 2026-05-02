namespace RunFence.Infrastructure;

public interface IProcessImageNameReader
{
    string? TryGetProcessImageFileName(uint processId);
}
