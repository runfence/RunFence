using Microsoft.Win32;

namespace RunFence.Core;

/// <summary>
/// Resolves profile-related filesystem paths for a given SID by reading from the
/// Windows registry ProfileList and the file system. Does not contact Active Directory.
/// </summary>
public class ProfilePathResolver(ILoggingService log) : IProfilePathResolver
{
    /// <inheritdoc/>
    public string? TryGetProfilePath(string sid)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                $@"{PathConstants.ProfileListRegistryKey}\{sid}");
            var raw = key?.GetValue("ProfileImagePath") as string;
            return string.IsNullOrEmpty(raw) ? null : Environment.ExpandEnvironmentVariables(raw);
        }
        catch (Exception ex)
        {
            log.Error($"Failed to read profile path for SID {sid}", ex);
            return null;
        }
    }

    /// <inheritdoc/>
    public string? TryResolveNameFromRegistry(string sid)
    {
        var path = TryGetProfilePath(sid);
        if (path == null)
            return null;
        var leaf = Path.GetFileName(path);
        return string.IsNullOrEmpty(leaf) ? null : leaf;
    }

    /// <inheritdoc/>
    public string? TryGetDesktopPath(string sid, bool isCurrentAccount)
    {
        if (isCurrentAccount)
            return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        var profilePath = TryGetProfilePath(sid);
        return profilePath == null ? null : Path.Combine(profilePath, "Desktop");
    }

    /// <inheritdoc/>
    public string? TryGetStartMenuProgramsPath(string sid, bool isCurrentAccount)
    {
        if (isCurrentAccount)
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Windows\Start Menu\Programs");
        }

        var profilePath = TryGetProfilePath(sid);
        if (profilePath == null)
            return null;
        return Path.Combine(profilePath, @"AppData\Roaming\Microsoft\Windows\Start Menu\Programs");
    }

    /// <inheritdoc/>
    public string? TryGetTaskBarPath(string sid, bool isCurrentAccount)
    {
        if (isCurrentAccount)
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
        }

        var profilePath = TryGetProfilePath(sid);
        if (profilePath == null)
            return null;
        return Path.Combine(profilePath,
            @"AppData\Roaming\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
    }
}
