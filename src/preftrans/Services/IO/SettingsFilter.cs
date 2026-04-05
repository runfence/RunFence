using PrefTrans.Settings;

namespace PrefTrans.Services.IO;

public static class SettingsFilter
{
    public static void FilterUserProfilePaths(UserSettings settings)
    {
        var profilePaths = UserProfileFilter.GetUserProfilePaths();
        if (profilePaths.Length == 0)
            return;

        // Environment: remove entries whose raw value contains literal profile or WindowsApps path
        if (settings.Environment?.Variables != null)
        {
            var toRemove = settings.Environment.Variables
                .Where(kv => UserProfileFilter.ContainsUserProfilePath(kv.Value.Value, profilePaths)
                             || UserProfileFilter.ContainsWindowsAppsPath(kv.Value.Value))
                .Select(kv => kv.Key)
                .ToList();
            foreach (var key in toRemove)
                settings.Environment.Variables.Remove(key);
            if (settings.Environment.Variables.Count == 0)
                settings.Environment.Variables = null;
        }

        // TrayIcons: remove entries with ExecutablePath inside profile or in WindowsApps
        if (settings.TrayIcons?.PerAppVisibility != null)
        {
            settings.TrayIcons.PerAppVisibility.RemoveAll(e => UserProfileFilter.ContainsUserProfilePath(e.ExecutablePath, profilePaths)
                                                               || UserProfileFilter.ContainsWindowsAppsPath(e.ExecutablePath));
            if (settings.TrayIcons.PerAppVisibility.Count == 0)
                settings.TrayIcons.PerAppVisibility = null;
        }

        // Notifications: remove per-app entries with app ID path inside profile
        if (settings.Notifications?.PerAppSuppression != null)
        {
            var toRemove = settings.Notifications.PerAppSuppression
                .Where(kv => UserProfileFilter.ContainsUserProfilePath(kv.Key, profilePaths))
                .Select(kv => kv.Key)
                .ToList();
            foreach (var key in toRemove)
                settings.Notifications.PerAppSuppression.Remove(key);
            if (settings.Notifications.PerAppSuppression.Count == 0)
                settings.Notifications.PerAppSuppression = null;
        }

        // Desktop: null out wallpaper if inside profile
        if (settings.Desktop != null &&
            UserProfileFilter.ContainsUserProfilePath(settings.Desktop.WallpaperPath, profilePaths))
        {
            settings.Desktop.WallpaperPath = null;
        }

        // ScreenSaver: null out executable path if inside profile or in WindowsApps
        if (settings.ScreenSaver != null &&
            (UserProfileFilter.ContainsUserProfilePath(settings.ScreenSaver.ExecutablePath, profilePaths)
             || UserProfileFilter.ContainsWindowsAppsPath(settings.ScreenSaver.ExecutablePath)))
        {
            settings.ScreenSaver.ExecutablePath = null;
        }

        // FileAssociations: remove UWP app associations (AppX/AUMID ProgIds, WindowsApps paths)
        // and entries whose open command references a user-profile-specific app
        if (settings.FileAssociations?.Associations != null)
        {
            var toRemove = settings.FileAssociations.Associations
                .Where(kv => UserProfileFilter.ContainsUserProfilePath(kv.Value.OpenCommand, profilePaths)
                             || UserProfileFilter.ContainsWindowsAppsPath(kv.Value.OpenCommand)
                             || UserProfileFilter.IsUwpProgId(kv.Value.ProgId))
                .Select(kv => kv.Key)
                .ToList();
            foreach (var key in toRemove)
                settings.FileAssociations.Associations.Remove(key);
            if (settings.FileAssociations.Associations.Count == 0)
                settings.FileAssociations.Associations = null;
        }

    }
}