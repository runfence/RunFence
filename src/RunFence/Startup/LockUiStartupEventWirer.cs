using RunFence.Infrastructure;

namespace RunFence.Startup;

public class LockUiStartupEventWirer(
    IUiThreadInvoker uiThreadInvoker,
    ILockUiEventSource lockManager,
    IWindowsHelloPinFallbackPromptEventSource fallbackPrompt,
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
        fallbackPrompt.WindowsHelloUnavailableConfirmRequested +=
            () => uiThreadInvoker.Invoke(mainForm.ConfirmWindowsHelloUnavailableFallback);
        fallbackPrompt.WindowsHelloFailedConfirmRequested +=
            () => uiThreadInvoker.Invoke(mainForm.ConfirmWindowsHelloFailedFallback);
    }
}
