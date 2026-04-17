namespace RunFence.Startup.UI;

public interface ILockManager
{
    bool IsLocked { get; }
    bool IsUnlockPolling { get; }
    void LockWindow();
    void Unlock();
    Task<bool> TryUnlockAsync(bool isAdmin);
}
