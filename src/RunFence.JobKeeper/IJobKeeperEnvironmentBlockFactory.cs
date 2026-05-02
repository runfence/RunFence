namespace RunFence.JobKeeper;

public interface IJobKeeperEnvironmentBlockFactory
{
    IntPtr Build(IReadOnlyDictionary<string, string> environment);
    void Free(IntPtr environmentBlock);
}
