namespace RunFence.ProfileKeeper;

public interface IProfileKeeperProcessTerminator
{
    void Terminate(int processId);
}
