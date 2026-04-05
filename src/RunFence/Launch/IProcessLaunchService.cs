using RunFence.Core.Models;

namespace RunFence.Launch;

public interface IProcessLaunchService
{
    void Launch(AppEntry app, LaunchCredentials credentials,
        string? launcherArguments, string? launcherWorkingDirectory = null,
        AppSettings? settings = null, LaunchFlags flags = default);

    void LaunchExe(ProcessLaunchTarget target, LaunchCredentials credentials,
        LaunchFlags flags = default);

    int LaunchExeReturnPid(ProcessLaunchTarget target, LaunchCredentials credentials,
        LaunchFlags flags = default);

    void LaunchUrl(string url, LaunchCredentials credentials, LaunchFlags flags = default);

    void LaunchFolder(string folderPath, string folderBrowserExe, string folderBrowserArgs,
        LaunchCredentials credentials, LaunchFlags flags = default);

    bool ValidateUrlScheme(string url, out string? error);
}