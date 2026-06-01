using System.Security;
namespace RunFence.Launching.Resolution;

public sealed class WindowsAppsAliasPathResolver(
    IExecutableFileSystem fileSystem,
    IProfilePathReader profilePathReader,
    IAppExecLinkReader appExecLinkReader)
    : IWindowsAppsAliasPathResolver
{
    public string? TryResolveForUserSid(string nameOrPath, string targetUserSid)
    {
        if (!TryGetAliasFileName(nameOrPath, out var fileName))
            return null;

        string? profilePath;
        try
        {
            profilePath = profilePathReader.GetProfilePath(targetUserSid);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
        {
            return null;
        }

        return profilePath == null
            ? null
            : TryResolveAliasPath(Path.Combine(profilePath, "AppData", "Local", "Microsoft", "WindowsApps", fileName));
    }

    public bool IsWindowsAppsAliasPath(string path)
    {
        try
        {
            if (!fileSystem.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
                return false;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
        {
            return false;
        }

        return appExecLinkReader.IsAppExecLink(path);
    }

    private string? TryResolveAliasPath(string windowsAppsExe)
    {
        try
        {
            return IsWindowsAppsAliasPath(windowsAppsExe) ? windowsAppsExe : null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
        {
            return null;
        }
    }

    private static bool TryGetAliasFileName(string nameOrPath, out string fileName)
    {
        fileName = string.Empty;
        if (string.IsNullOrWhiteSpace(nameOrPath)
            || Path.IsPathRooted(nameOrPath)
            || !string.IsNullOrEmpty(Path.GetDirectoryName(nameOrPath)))
        {
            return false;
        }

        fileName = nameOrPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? nameOrPath
            : nameOrPath + ".exe";
        return true;
    }
}
