using RunFence.Account;
using RunFence.Core.Models;

namespace RunFence.Launch;

/// <summary>
/// Decorator for <see cref="IAppLaunchOrchestrator"/> that transparently applies profile repair
/// around every launch call, removing the need for callers to inject and invoke
/// <see cref="IProfileRepairHelper"/> individually.
/// </summary>
public class ProfileRepairLaunchDecorator(
    IAppLaunchOrchestrator inner,
    IProfileRepairHelper profileRepair) : IAppLaunchOrchestrator
{
    public void SetData(SessionContext session) => inner.SetData(session);

    public void Launch(AppEntry app, string? launcherArguments, string? launcherWorkingDirectory = null)
        => profileRepair.ExecuteWithProfileRepair(
            () => inner.Launch(app, launcherArguments, launcherWorkingDirectory),
            app.AccountSid);

    public void LaunchFolderBrowser(string accountSid, string folderPath,
        bool? launchAsLowIntegrity = null, bool? useSplitToken = null)
        => profileRepair.ExecuteWithProfileRepair(
            () => inner.LaunchFolderBrowser(accountSid, folderPath, launchAsLowIntegrity, useSplitToken),
            accountSid);

    public void LaunchFolderBrowserFromTray(string accountSid, Func<string, bool?>? permissionPrompt = null,
        bool? useSplitToken = null, bool? useLowIntegrity = null)
        => profileRepair.ExecuteWithProfileRepair(
            () => inner.LaunchFolderBrowserFromTray(accountSid, permissionPrompt, useSplitToken, useLowIntegrity),
            accountSid);

    public void LaunchTerminalFromTray(string accountSid, bool? useSplitToken = null, bool? useLowIntegrity = null)
        => profileRepair.ExecuteWithProfileRepair(
            () => inner.LaunchTerminalFromTray(accountSid, useSplitToken, useLowIntegrity),
            accountSid);

    public void LaunchDiscoveredApp(string exePath, string accountSid)
        => profileRepair.ExecuteWithProfileRepair(
            () => inner.LaunchDiscoveredApp(exePath, accountSid),
            accountSid);

    public void LaunchExe(string exePath, string accountSid, List<string>? arguments = null, string? workingDirectory = null)
        => profileRepair.ExecuteWithProfileRepair(
            () => inner.LaunchExe(exePath, accountSid, arguments, workingDirectory),
            accountSid);

    public int LaunchExeReturnPid(string exePath, string accountSid, List<string>? arguments = null,
        string? workingDirectory = null, Func<string, bool?>? permissionPrompt = null,
        bool? useSplitToken = null, bool hideWindow = false)
    {
        var pid = 0;
        profileRepair.ExecuteWithProfileRepair(
            () => { pid = inner.LaunchExeReturnPid(exePath, accountSid, arguments, workingDirectory, permissionPrompt, useSplitToken, hideWindow); },
            accountSid);
        return pid;
    }
}