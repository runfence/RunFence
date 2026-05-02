using System.Runtime.InteropServices;
using RunFence.Launching.Environment;

namespace RunFence.JobKeeper;

public sealed class JobKeeperEnvironmentBlockFactory : IJobKeeperEnvironmentBlockFactory
{
    public IntPtr Build(IReadOnlyDictionary<string, string> environment) => EnvironmentBlockBuilder.Build(environment);

    public void Free(IntPtr environmentBlock) => Marshal.FreeHGlobal(environmentBlock);
}
