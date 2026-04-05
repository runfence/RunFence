using RunFence.Core.Models;
using RunFence.Startup.UI;

namespace RunFence.Infrastructure;

public class ApplicationState : IAppStateProvider, IAppLockControl, IDataChangeNotifier
{
    private readonly ISessionProvider _sessionProvider;
    private readonly LockManager _lockManager;
    private readonly IModalTracker _modalTracker;

    private volatile bool _isShuttingDown;

    public ApplicationState(ISessionProvider sessionProvider, LockManager lockManager, IModalTracker modalTracker)
    {
        _sessionProvider = sessionProvider;
        _lockManager = lockManager;
        _modalTracker = modalTracker;
    }

    public OperationGuard EnforcementGuard { get; } = new();

    // IAppStateProvider
    public bool IsShuttingDown
    {
        get => _isShuttingDown;
        set => _isShuttingDown = value;
    }

    /// <summary>
    /// True when an enforcement operation (ACL/shortcut reapply) is in progress.
    /// Note: account panel operations are short-lived and not tracked here.
    /// </summary>
    public bool IsOperationInProgress => EnforcementGuard.IsInProgress;

    /// <summary>True when any panel modal dialog is currently open.</summary>
    public bool IsModalOpen => _modalTracker.AnyModalOpen;

    public AppDatabase Database => _sessionProvider.GetSession().Database;

    // IAppLockControl
    public bool IsLocked => _lockManager.IsLocked;
    public bool IsUnlockPolling => _lockManager.IsUnlockPolling;
    public void Lock() => _lockManager.LockWindow();
    public void Unlock() => _lockManager.Unlock();
    public bool TryUnlock(bool isAdmin) => _lockManager.TryUnlock(isAdmin);

    // IDataChangeNotifier
    public event Action? DataChanged;
    public void NotifyDataChanged() => DataChanged?.Invoke();
}