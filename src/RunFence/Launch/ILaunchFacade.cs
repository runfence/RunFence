using RunFence.Launch.Tokens;

namespace RunFence.Launch;

public interface ILaunchFacade
{
    ProcessInfo? LaunchFile(ProcessLaunchTarget target, LaunchIdentity identity, Func<string, string, bool>? permissionPrompt = null);
    ProcessInfo? LaunchFile(string exePath, LaunchIdentity identity, string? arguments = null, Func<string, string, bool>? permissionPrompt = null)
        => LaunchFile(new ProcessLaunchTarget(exePath, arguments), identity, permissionPrompt);
    ProcessInfo? LaunchFolderBrowser(LaunchIdentity identity, string? folderPath = null,
        Func<string, string, bool>? folderPermissionPrompt = null);
    void LaunchUrl(string url, LaunchIdentity identity);
}
