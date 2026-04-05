using RunFence.Core.Models;

namespace RunFence.Launch;

public interface IAppLaunchOrchestrator
{
    void SetData(SessionContext session);
    void Launch(AppEntry app, string? launcherArguments, string? launcherWorkingDirectory = null);
    void LaunchFolderBrowser(string accountSid, string folderPath, bool? launchAsLowIntegrity = null, bool? useSplitToken = null);

    void LaunchFolderBrowserFromTray(string accountSid, Func<string, bool?>? permissionPrompt = null,
        bool? useSplitToken = null, bool? useLowIntegrity = null);

    void LaunchTerminalFromTray(string accountSid, bool? useSplitToken = null, bool? useLowIntegrity = null);
    void LaunchDiscoveredApp(string exePath, string accountSid);
    void LaunchExe(string exePath, string accountSid, List<string>? arguments = null, string? workingDirectory = null);

    int LaunchExeReturnPid(string exePath, string accountSid, List<string>? arguments = null,
        string? workingDirectory = null, Func<string, bool?>? permissionPrompt = null,
        bool? useSplitToken = null, bool hideWindow = false);
}