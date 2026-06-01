using RunFence.Core;

namespace RunFence.Account.UI;

/// <summary>
/// Resolves tool executables for a given account SID.
/// </summary>
public class AccountToolResolver(IProfilePathResolver profilePathResolver)
{
    public string? GetProfileRoot(string sid)
    {
        if (SidResolutionHelper.IsSystemSid(sid))
            return null;
        return SidResolutionHelper.GetCurrentUserSid() == sid
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : profilePathResolver.TryGetProfilePath(sid);
    }

    public string? ResolveWindowsAppsExe(string sid, string exeName)
    {
        var localAppData = SidResolutionHelper.GetCurrentUserSid() == sid
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : profilePathResolver.TryGetProfilePath(sid) is { } profilePath
                ? Path.Combine(profilePath, "AppData", "Local")
                : null;

        if (localAppData == null)
            return null;
        var exePath = Path.Combine(localAppData, "Microsoft", "WindowsApps", exeName);
        return File.Exists(exePath) ? exePath : null;
    }
}
