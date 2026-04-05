using Microsoft.Win32;
using RunFence.Core;

namespace RunFence.Infrastructure;

public static class FileDialogHelper
{
    /// <summary>
    /// Adds custom places to a file dialog: interactive user's Start Menu and Profile,
    /// the system drive root, and the application's ProgramData directory.
    /// </summary>
    public static void AddInteractiveUserCustomPlaces(FileDialog dlg)
    {
        var sid = NativeTokenHelper.TryGetInteractiveUserSid();
        if (sid != null)
        {
            var profilePath = TryGetProfilePath(sid.ToString());
            if (profilePath != null)
            {
                TryAdd(dlg, Path.Combine(profilePath, @"AppData\Roaming\Microsoft\Windows\Start Menu"));
                TryAdd(dlg, profilePath);
            }
        }

        TryAdd(dlg, Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\");
        TryAdd(dlg, Constants.ProgramDataDir);
    }

    private static string? TryGetProfilePath(string sid)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"{Constants.ProfileListRegistryKey}\{sid}");
            var raw = key?.GetValue("ProfileImagePath") as string;
            return string.IsNullOrEmpty(raw) ? null : Environment.ExpandEnvironmentVariables(raw);
        }
        catch
        {
            return null;
        }
    }

    private static void TryAdd(FileDialog dlg, string path)
    {
        try
        {
            dlg.CustomPlaces.Add(new FileDialogCustomPlace(path));
        }
        catch
        {
        }
    }
}