namespace RunFence.Infrastructure;

public interface ITimerScheduler
{
    IDisposable Schedule(Action callback, int intervalMs);
}