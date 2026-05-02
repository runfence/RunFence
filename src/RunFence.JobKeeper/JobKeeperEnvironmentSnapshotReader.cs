using RunFence.Launching.Environment;

namespace RunFence.JobKeeper;

public sealed class JobKeeperEnvironmentSnapshotReader(IJobKeeperEnvironmentNativeApi nativeApi)
    : IJobKeeperEnvironmentSnapshotReader
{
    public Dictionary<string, string> ReadAll()
    {
        if (!nativeApi.OpenCurrentProcessToken(out var tokenHandle))
            return ProcessEnvironmentVariableReader.ReadAll();

        try
        {
            if (!nativeApi.CreateEnvironmentBlock(out var environmentBlock, tokenHandle))
                return ProcessEnvironmentVariableReader.ReadAll();

            try
            {
                return NativeEnvironmentBlockReader.Read(environmentBlock);
            }
            finally
            {
                nativeApi.DestroyEnvironmentBlock(environmentBlock);
            }
        }
        finally
        {
            nativeApi.CloseHandle(tokenHandle);
        }
    }
}
