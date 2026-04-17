namespace RunFence.Startup.UI;

public interface IAutoLockTimerService
{
    void Start(int timeoutSeconds, Action onTimeout);
    void Stop();
}
