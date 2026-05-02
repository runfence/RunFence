using System.Diagnostics;

namespace RunFence.Infrastructure;

public sealed class JobKeeperProcessTerminator : IJobKeeperProcessTerminator
{
    private const int ExitWaitMilliseconds = 5000;

    public void Kill(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.Kill();
            process.WaitForExit(ExitWaitMilliseconds);
        }
        catch { }
    }
}
