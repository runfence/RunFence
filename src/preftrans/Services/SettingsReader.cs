using PrefTrans.Services.IO;
using PrefTrans.Settings;

namespace PrefTrans.Services;

public static class SettingsReader
{
    public static UserSettings ReadAll()
    {
        var settings = new UserSettings
        {
            Mouse = MouseSettingsIO.Read(),
            Keyboard = KeyboardSettingsIO.Read(),
            Scroll = ScrollSettingsIO.Read(),
            Explorer = ExplorerSettingsIO.Read(),
            Desktop = DesktopSettingsIO.Read(),
            Taskbar = TaskbarSettingsIO.Read(),
            Theme = ThemeSettingsIO.Read(),
            ScreenSaver = ScreenSaverSettingsIO.Read(),
            InputLanguage = InputLanguageSettingsIO.Read(),
            Accessibility = AccessibilitySettingsIO.Read(),
            Regional = RegionalSettingsIO.Read(),
            TrayIcons = TrayIconsSettingsIO.Read(),
            Notifications = NotificationsSettingsIO.Read(),
            UserFolders = UserFoldersSettingsIO.Read(),
            Environment = EnvironmentSettingsIO.Read(),
            FileAssociations = FileAssociationsSettingsIO.Read(),
            NightLight = NightLightSettingsIO.Read(),
        };

        SettingsFilter.FilterUserProfilePaths(settings);
        return settings;
    }
}