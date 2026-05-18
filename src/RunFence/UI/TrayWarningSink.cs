using RunFence.Infrastructure;
using RunFence.TrayIcon;

namespace RunFence.UI;

public sealed class TrayWarningSink(IUiThreadInvoker uiThreadInvoker, TrayIconManager trayIconManager) : ITrayWarningSink
{
    public void ShowWarning(string text)
        => uiThreadInvoker.BeginInvoke(() => trayIconManager.ShowBalloonTip(text));
}
