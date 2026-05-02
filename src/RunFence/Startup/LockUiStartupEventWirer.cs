using RunFence.Infrastructure;

namespace RunFence.Startup;

public class LockUiStartupEventWirer(
    IUiThreadInvoker uiThreadInvoker,
    ILockUiEventSource lockManager,
    IMainFormLockTarget mainForm) : IStartupEventWirer
{
    public void WireEvents()
    {
        lockManager.ShowWindowRequested +=
            () => uiThreadInvoker.BeginInvoke(mainForm.ShowWindowNormal);
        lockManager.ShowWindowUnlockedRequested +=
            () => uiThreadInvoker.BeginInvoke(mainForm.ShowWindowUnlocked);
        lockManager.WindowlessUnlockCompleted +=
            () => uiThreadInvoker.BeginInvoke(mainForm.HandleWindowlessUnlock);
        lockManager.WindowsHelloUnavailableConfirmRequested +=
            () => uiThreadInvoker.Invoke(mainForm.ConfirmWindowsHelloUnavailableFallback);
        lockManager.WindowsHelloFailedConfirmRequested +=
            () => uiThreadInvoker.Invoke(mainForm.ConfirmWindowsHelloFailedFallback);
    }
}
