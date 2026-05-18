using System.Diagnostics;

namespace RunFence.ProfileKeeper;

public sealed class ProfileKeeperProcessTerminator : IProfileKeeperProcessTerminator
{
    private const int ExitWaitMilliseconds = 5_000;

    public void Terminate(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill();
            process.WaitForExit(ExitWaitMilliseconds);
        }
        catch
        {
        }
    }
}
