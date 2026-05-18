using RunFence.Core.Models;
using RunFence.Startup.UI;

namespace RunFence.Infrastructure;

public class ApplicationState(
    ISessionProvider sessionProvider,
    ILockManager lockManager,
    IModalTracker modalTracker,
    UiThreadDatabaseAccessor dbAccessor)
    : IAppStateProvider, IAppLockControl, IDataChangeNotifier, IApplicationDataChangeSource
{
    private volatile bool _isShuttingDown;

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
    public bool IsModalOpen => modalTracker.AnyModalOpen;

    public AppDatabase Database => sessionProvider.GetSession().Database;
    public T ReadDatabase<T>(Func<AppDatabase, T> reader)
        => dbAccessor.Read(reader);

    public void ReadDatabase(Action<AppDatabase> reader)
        => dbAccessor.Read(reader);

    public AppDatabase CreateDatabaseSnapshot()
        => dbAccessor.CreateSnapshot();

    // IAppLockControl
    public bool IsLocked => lockManager.IsLocked;
    public bool IsUnlockPolling => lockManager.IsUnlockPolling;
    public void Lock() => lockManager.LockWindow();
    public void Unlock() => lockManager.Unlock();
    public Task<bool> TryUnlockAsync(bool isAdmin) => lockManager.TryUnlockAsync(isAdmin);
    public Task<bool> TryUnlockForOperationAsync(bool isAdmin) => lockManager.TryUnlockForOperationAsync(isAdmin);
    public Task<OperationUnlockResult> TryUnlockForOperationWithResultAsync(bool isAdmin) =>
        lockManager.TryUnlockForOperationWithResultAsync(isAdmin);

    // IDataChangeNotifier
    public event Action? DataChanged;
    public void NotifyDataChanged() => DataChanged?.Invoke();
}
