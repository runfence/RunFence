namespace RunFence.Infrastructure;

public interface IIdleMonitorService : IDisposable
{
    event Action? IdleTimeoutReached;
    void Configure(int timeoutMinutes);
    void Start();
    void Stop();
    void ResetIdleTimer();
}