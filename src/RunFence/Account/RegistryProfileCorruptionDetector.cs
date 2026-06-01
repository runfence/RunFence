using Microsoft.Win32;
using RunFence.Core;

namespace RunFence.Account;

/// <summary>Detects User Profile Service TEMP profile corruption for one account SID.</summary>
public class RegistryProfileCorruptionDetector : IProfileCorruptionDetector
{
    public CorruptedProfile? Detect(string sid)
    {
        try
        {
            using var profileList = Registry.LocalMachine.OpenSubKey(PathConstants.ProfileListRegistryKey);
            if (profileList == null)
                return null;

            var bakKeyName = sid + ".bak";
            using var bakKey = profileList.OpenSubKey(bakKeyName);
            if (bakKey == null)
                return null;

            using var activeKey = profileList.OpenSubKey(sid);

            var originalPath = bakKey.GetValue("ProfileImagePath") as string;
            var tempPath = activeKey?.GetValue("ProfileImagePath") as string;

            if (string.IsNullOrEmpty(originalPath))
                return null;

            if (string.IsNullOrEmpty(tempPath) || !IsTempProfilePath(tempPath))
                return null;

            if (IsTempProfilePath(originalPath))
                return null;

            if (!Directory.Exists(originalPath))
                return null;

            return new CorruptedProfile(sid, originalPath, tempPath);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsTempProfilePath(string profilePath)
    {
        var folderName = Path.GetFileName(profilePath.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(folderName))
            return false;

        var machineName = Environment.MachineName;
        if (string.Equals(folderName, "TEMP", StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.Equals(folderName, $"TEMP.{machineName}", StringComparison.OrdinalIgnoreCase))
            return true;

        var prefix = $"TEMP.{machineName}.";
        if (!folderName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var suffix = folderName[prefix.Length..];
        return suffix.Length > 0 && suffix.All(char.IsDigit);
    }
}
