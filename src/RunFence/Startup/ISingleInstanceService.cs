namespace RunFence.Startup;

public interface ISingleInstanceService : IDisposable
{
    bool TryAcquire();
}