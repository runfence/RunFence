using RunFence.Core.Models;

namespace RunFence.TrayIcon;

public interface ITrayMenuActionHandler
{
    void LaunchConfiguredApp(AppEntry app);
    void LaunchFolderBrowser(string accountSid, bool shift);
    void LaunchTerminal(string accountSid, bool shift);
    void LaunchDiscoveredApp(string exePath, string accountSid);
    void ExitApplication();
}
