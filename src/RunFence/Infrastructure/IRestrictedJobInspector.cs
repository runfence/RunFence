namespace RunFence.Infrastructure;

public interface IRestrictedJobInspector
{
    bool IsProcessInHandleLimitedJob(int pid);
}
