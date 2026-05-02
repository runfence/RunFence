namespace RunFence.Infrastructure;

public interface IJobKeeperProcessTerminator
{
    void Kill(int pid);
}
