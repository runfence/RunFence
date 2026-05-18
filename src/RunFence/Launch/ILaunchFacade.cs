using RunFence.Launch.Tokens;

namespace RunFence.Launch;

public interface ILaunchFacade
{
    LaunchExecutionResult LaunchFile(ProcessLaunchTarget target, LaunchIdentity identity, Func<string, string, bool>? permissionPrompt = null);
    LaunchExecutionResult LaunchFile(string exePath, LaunchIdentity identity, string? arguments = null, Func<string, string, bool>? permissionPrompt = null)
        => LaunchFile(new ProcessLaunchTarget(exePath, arguments), identity, permissionPrompt);
    LaunchExecutionResult LaunchFolderBrowser(LaunchIdentity identity, string? folderPath = null,
        Func<string, string, bool>? folderPermissionPrompt = null, bool isTargetApproved = true);
    LaunchExecutionResult LaunchUrl(string url, LaunchIdentity identity);
}
