namespace RunFence.Infrastructure;

public interface IProcessCreationTimeReader
{
    bool TryGetProcessCreationTimeUtcTicks(uint processId, out long creationTimeUtcTicks);
}
