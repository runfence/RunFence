namespace RunFence.JobKeeper;

internal sealed class JobKeeperRunner(JobKeeperPipeClientLoop pipeLoop)
{
    public void Run()
    {
        while (true)
        {
            pipeLoop.RunOnce();
            Thread.Sleep(500);
        }
    }
}
