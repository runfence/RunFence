namespace RunFence.JobKeeper;

internal sealed class JobKeeperRunner(JobKeeperPipeClientLoop pipeLoop)
{
    public void Run()
    {
        while (pipeLoop.RunOnce())
        {
            Thread.Sleep(500);
        }
    }
}
