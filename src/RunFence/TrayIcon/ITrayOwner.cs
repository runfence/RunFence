namespace RunFence.TrayIcon;

public interface ITrayOwner
{
    Task TryShowWindowAsync();
    void LockToTrayImmediately();
    bool IsLocked { get; }
    bool IsTrayLockVisible { get; }
    bool IsTrayLockEnabled { get; }
}
