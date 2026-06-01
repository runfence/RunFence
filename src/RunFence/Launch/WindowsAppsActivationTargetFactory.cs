using RunFence.Core;
using RunFence.Launching.Resolution;
namespace RunFence.Launch;

public sealed class WindowsAppsActivationTargetFactory(
    IWindowsAppsPackageIdentityResolver packageIdentityResolver,
    IProfilePathResolver profilePathResolver,
    string appxLauncherExePath) : IWindowsAppsActivationTargetFactory
{
    public WindowsAppsActivationTarget? TryCreate(
        ProcessLaunchTarget failedTarget,
        string packageIdentitySourcePath,
        string targetSid)
    {
        if (!packageIdentityResolver.TryResolvePackageIdentity(packageIdentitySourcePath, out var packageResolution))
            return null;

        var resultDirectoryPath = BuildResultDirectoryPath(targetSid);
        if (string.IsNullOrWhiteSpace(resultDirectoryPath))
            return null;

        var resultFilePath = Path.Combine(resultDirectoryPath, "result.jsonl");
        var prefix = CommandLineHelper.MaterializeProcessArguments(
            [resultFilePath, packageResolution.PackageExecutablePath]) ?? string.Empty;
        var arguments = string.IsNullOrEmpty(failedTarget.Arguments)
            ? prefix
            : prefix + " " + failedTarget.Arguments;

        return new WindowsAppsActivationTarget(
            new ProcessLaunchTarget(
                appxLauncherExePath,
                arguments,
                WorkingDirectory: AppContext.BaseDirectory,
                HideWindow: true,
                SuppressStartupFeedback: true),
            resultDirectoryPath,
            resultFilePath,
            packageResolution.PackageExecutablePath,
            failedTarget.Arguments ?? string.Empty);
    }

    private string? BuildResultDirectoryPath(string targetSid)
    {
        var localAppData = ResolveLocalAppDataPath(targetSid);
        if (string.IsNullOrWhiteSpace(localAppData))
            return null;

        return Path.Combine(
            localAppData,
            PathConstants.AppName,
            "Logs",
            $"appx-launch-{Guid.NewGuid():N}");
    }

    private string? ResolveLocalAppDataPath(string targetSid)
    {
        if (string.Equals(targetSid, SidResolutionHelper.GetCurrentUserSid(), StringComparison.OrdinalIgnoreCase))
            return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var profilePath = profilePathResolver.TryGetProfilePath(targetSid);
        return string.IsNullOrWhiteSpace(profilePath)
            ? null
            : Path.Combine(profilePath, "AppData", "Local");
    }
}
