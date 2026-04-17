namespace RunFence.Infrastructure;

public interface IAppLockControl
{
    bool IsLocked { get; }
    bool IsUnlockPolling { get; }
    void Lock();
    void Unlock();
    Task<bool> TryUnlockAsync(bool isAdmin);
}