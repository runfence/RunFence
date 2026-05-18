using RunFence.Core.Models;

namespace RunFence.Startup.UI;

public interface ILockStateService
{
    bool IsLocked { get; }
    bool AutoLockEnabled { get; }
    event Action? Locked;
    event Action? Unlocked;
    void Lock();
    void Unlock();
}

public class LockStateService(SessionContext session) : ILockStateService
{
    private volatile bool _isLocked;

    public bool IsLocked => _isLocked;
    public bool AutoLockEnabled => session.Database.Settings.AutoLockInBackground;
    public event Action? Locked;
    public event Action? Unlocked;

    public void Lock()
    {
        if (_isLocked)
            return;

        _isLocked = true;
        Locked?.Invoke();
    }

    public void Unlock()
    {
        if (!_isLocked)
            return;

        _isLocked = false;
        Unlocked?.Invoke();
    }
}

