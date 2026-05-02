using System.Text;
using RunFence.Launching.Environment;
using RunFence.Launching.Windows;

namespace RunFence.Launching.Resolution;

public sealed class ExecutablePathResolver(
    IExecutableFileSystem fileSystem,
    IProfilePathReader profilePathReader)
    : IExecutablePathResolver
{
    public string? TryResolvePath(string nameOrPath, ExecutablePathResolutionContext context)
    {
        if (string.IsNullOrWhiteSpace(nameOrPath))
            return null;

        if (Path.IsPathRooted(nameOrPath))
            return fileSystem.FileExists(nameOrPath) ? nameOrPath : null;

        if (!string.IsNullOrEmpty(Path.GetDirectoryName(nameOrPath)))
            return null;

        var fileName = EnsureExeExtension(nameOrPath);

        if (context.EnvironmentReader != null)
        {
            if (context.EnvironmentReader.TryGetValue("PATH", out var envPath)
                && !string.IsNullOrWhiteSpace(envPath))
            {
                var foundInEnvironment = TrySearchPath(envPath, fileName);
                if (foundInEnvironment != null)
                    return foundInEnvironment;
            }

            var foundInEnvironmentWindowsApps = TryResolveWindowsAppsAlias(context.EnvironmentReader, fileName);
            if (foundInEnvironmentWindowsApps != null)
                return foundInEnvironmentWindowsApps;
        }
        else if (context.SearchCurrentProcessPath)
        {
            var foundInProcessPath = TrySearchPath(null, fileName);
            if (foundInProcessPath != null)
                return foundInProcessPath;
        }

        if (context.TargetUserSid != null)
            return TryResolveWindowsAppsAlias(context.TargetUserSid, fileName);

        return null;
    }

    private string? TryResolveWindowsAppsAlias(IEnvironmentVariableReader environmentReader, string fileName)
    {
        var localAppData = GetLocalAppData(environmentReader);
        if (localAppData == null)
            return null;

        var windowsAppsExe = Path.Combine(localAppData, "Microsoft", "WindowsApps", fileName);
        return fileSystem.FileExists(windowsAppsExe) ? windowsAppsExe : null;
    }

    private static string? GetLocalAppData(IEnvironmentVariableReader environmentReader)
    {
        if (environmentReader.TryGetValue("LOCALAPPDATA", out var localAppData)
            && !string.IsNullOrWhiteSpace(localAppData))
        {
            return localAppData;
        }

        return environmentReader.TryGetValue("USERPROFILE", out var userProfile)
               && !string.IsNullOrWhiteSpace(userProfile)
            ? Path.Combine(userProfile, "AppData", "Local")
            : null;
    }

    private string? TryResolveWindowsAppsAlias(string targetUserSid, string fileName)
    {
        var profilePath = profilePathReader.GetProfilePath(targetUserSid);
        if (profilePath == null)
            return null;

        var windowsAppsExe = Path.Combine(profilePath, "AppData", "Local", "Microsoft", "WindowsApps", fileName);
        return fileSystem.FileExists(windowsAppsExe) ? windowsAppsExe : null;
    }

    private static string EnsureExeExtension(string nameOrPath) =>
        nameOrPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? nameOrPath
            : nameOrPath + ".exe";

    private string? TrySearchPath(string? path, string fileName)
    {
        var buffer = new StringBuilder(260);
        var result = LaunchingNative.SearchPath(path, fileName, null, (uint)buffer.Capacity, buffer, out _);
        if (result == 0)
            return null;

        if (result >= (uint)buffer.Capacity)
        {
            buffer = new StringBuilder((int)result + 1);
            result = LaunchingNative.SearchPath(path, fileName, null, (uint)buffer.Capacity, buffer, out _);
            if (result == 0)
                return null;
        }

        return buffer.ToString();
    }
}
