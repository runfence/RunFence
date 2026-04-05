using Microsoft.Win32;
using PrefTrans.Native;
using PrefTrans.Settings;

namespace PrefTrans.Services.IO;

public static class UserFoldersSettingsIO
{
    public static UserFoldersSettings Read()
    {
        var userFolders = new UserFoldersSettings();
        SafeExecutor.Try(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.RegUserShellFolders);
            if (key == null)
                return;

            var profilePaths = UserProfileFilter.GetUserProfilePaths();

            string? ReadIfNonDefault(string valueName, string defaultSuffix)
            {
                if (key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string val)
                    return null;
                if (string.Equals(val, @"%USERPROFILE%\" + defaultSuffix, StringComparison.OrdinalIgnoreCase))
                    return null;
                // Skip values containing the literal source profile path
                if (UserProfileFilter.ContainsUserProfilePath(val, profilePaths))
                    return null;
                // Keep only %USERPROFILE%\... ExpandString values; skip non-standard absolute paths
                if (!val.StartsWith(@"%USERPROFILE%\", StringComparison.OrdinalIgnoreCase)
                    && !val.StartsWith("%USERPROFILE%/", StringComparison.OrdinalIgnoreCase))
                    return null;
                return val;
            }

            userFolders.Desktop = ReadIfNonDefault("Desktop", "Desktop");
            userFolders.Documents = ReadIfNonDefault("Personal", "Documents");
            userFolders.Downloads = ReadIfNonDefault("{374DE290-123F-4565-9164-39C4925E467B}", "Downloads");
            userFolders.Music = ReadIfNonDefault("My Music", "Music");
            userFolders.Pictures = ReadIfNonDefault("My Pictures", "Pictures");
            userFolders.Videos = ReadIfNonDefault("My Video", "Videos");
        }, "reading");
        return userFolders;
    }

    public static void Write(UserFoldersSettings userFolders)
    {
        bool changed = false;
        SafeExecutor.Try(() =>
        {
            using var key = Registry.CurrentUser.CreateSubKey(Constants.RegUserShellFolders);

            void Set(string name, string? val)
            {
                if (val == null)
                    return;
                // Expand environment variables to check if the resolved path exists
                var expanded = Environment.ExpandEnvironmentVariables(val);
                if (Path.IsPathFullyQualified(expanded) && !Directory.Exists(expanded))
                {
                    Console.Error.WriteLine($"Warning: skipping user folder {name} — path does not exist: {expanded}");
                    return;
                }

                key.SetValue(name, val, RegistryValueKind.ExpandString);
                changed = true;
            }

            Set("Desktop", userFolders.Desktop);
            Set("Personal", userFolders.Documents);
            Set("{374DE290-123F-4565-9164-39C4925E467B}", userFolders.Downloads);
            Set("My Music", userFolders.Music);
            Set("My Pictures", userFolders.Pictures);
            Set("My Video", userFolders.Videos);
        }, "writing");
        if (changed)
        {
            NativeMethods.SHChangeNotify(Constants.SHCNE_ALLEVENTS, Constants.SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            BroadcastHelper.Broadcast();
        }
    }
}