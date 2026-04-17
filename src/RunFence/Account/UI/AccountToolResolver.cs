using RunFence.Core;

namespace RunFence.Account.UI;

/// <summary>
/// Resolves tool executables for a given account SID.
/// </summary>
public class AccountToolResolver(ISidResolver sidResolver)
{
    /// <summary>
    /// Returns wt.exe from the account's WindowsApps directory if installed, otherwise falls back to cmd.exe.
    /// </summary>
    public string ResolveTerminalExe(string sid)
        => ResolveWindowsAppsExe(sid, "wt.exe") ?? "cmd.exe";

    public string? GetProfileRoot(string sid)
        => SidResolutionHelper.GetCurrentUserSid() == sid
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : sidResolver.TryGetProfilePath(sid);

    public string? ResolveWindowsAppsExe(string sid, string exeName)
    {
        var localAppData = SidResolutionHelper.GetCurrentUserSid() == sid
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : sidResolver.TryGetProfilePath(sid) is { } profilePath
                ? Path.Combine(profilePath, "AppData", "Local")
                : null;

        if (localAppData == null)
            return null;
        var exePath = Path.Combine(localAppData, "Microsoft", "WindowsApps", exeName);
        return File.Exists(exePath) ? exePath : null;
    }
}
