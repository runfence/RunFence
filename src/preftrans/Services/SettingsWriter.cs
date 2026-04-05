using PrefTrans.Services.IO;
using PrefTrans.Settings;

namespace PrefTrans.Services;

public static class SettingsWriter
{
    public static void WriteAll(UserSettings settings)
    {
        if (settings.Mouse != null)
            MouseSettingsIO.Write(settings.Mouse);
        if (settings.Keyboard != null)
            KeyboardSettingsIO.Write(settings.Keyboard);
        if (settings.Scroll != null)
            ScrollSettingsIO.Write(settings.Scroll);
        if (settings.Explorer != null)
            ExplorerSettingsIO.Write(settings.Explorer);
        if (settings.Desktop != null)
            DesktopSettingsIO.Write(settings.Desktop);
        if (settings.Taskbar != null)
            TaskbarSettingsIO.Write(settings.Taskbar);
        if (settings.Theme != null)
            ThemeSettingsIO.Write(settings.Theme);
        if (settings.ScreenSaver != null)
            ScreenSaverSettingsIO.Write(settings.ScreenSaver);
        if (settings.InputLanguage != null)
            InputLanguageSettingsIO.Write(settings.InputLanguage);
        if (settings.Accessibility != null)
            AccessibilitySettingsIO.Write(settings.Accessibility);
        if (settings.Regional != null)
            RegionalSettingsIO.Write(settings.Regional);
        if (settings.TrayIcons != null)
            TrayIconsSettingsIO.Write(settings.TrayIcons);
        if (settings.Notifications != null)
            NotificationsSettingsIO.Write(settings.Notifications);
        if (settings.UserFolders != null)
            UserFoldersSettingsIO.Write(settings.UserFolders);
        if (settings.Environment != null)
            EnvironmentSettingsIO.Write(settings.Environment);
        if (settings.FileAssociations != null)
            FileAssociationsSettingsIO.Write(settings.FileAssociations);
        if (settings.NightLight != null)
            NightLightSettingsIO.Write(settings.NightLight);
    }
}