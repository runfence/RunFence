using Microsoft.Win32;
using PrefTrans.Native;
using PrefTrans.Settings;

namespace PrefTrans.Services.IO;

public static class ThemeSettingsIO
{
    public static ThemeSettings Read()
    {
        var theme = new ThemeSettings();
        SafeExecutor.Try(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.RegThemesPersonalize);
            if (key == null)
                return;
            theme.AppsUseLightTheme = key.GetValue("AppsUseLightTheme") as int?;
            theme.SystemUsesLightTheme = key.GetValue("SystemUsesLightTheme") as int?;
            theme.EnableTransparency = key.GetValue("EnableTransparency") as int?;
            theme.ColorPrevalence = key.GetValue("ColorPrevalence") as int?;
        }, "reading");
        SafeExecutor.Try(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.RegDWM);
            var val = key?.GetValue("AccentColor");
            if (val is int i)
                theme.AccentColor = unchecked((uint)i);
        }, "reading");
        return theme;
    }

    public static void Write(ThemeSettings theme)
    {
        bool changed = false;
        SafeExecutor.Try(() =>
        {
            using var key = Registry.CurrentUser.CreateSubKey(Constants.RegThemesPersonalize);

            void Set(string name, int? val)
            {
                if (val.HasValue)
                {
                    key.SetValue(name, val.Value, RegistryValueKind.DWord);
                    changed = true;
                }
            }

            Set("AppsUseLightTheme", theme.AppsUseLightTheme);
            Set("SystemUsesLightTheme", theme.SystemUsesLightTheme);
            Set("EnableTransparency", theme.EnableTransparency);
            Set("ColorPrevalence", theme.ColorPrevalence);
        }, "writing");
        SafeExecutor.Try(() =>
        {
            if (theme.AccentColor.HasValue)
            {
                using var key = Registry.CurrentUser.CreateSubKey(Constants.RegDWM);
                key.SetValue("AccentColor", unchecked((int)theme.AccentColor.Value), RegistryValueKind.DWord);
                changed = true;
            }
        }, "writing");
        if (changed)
            BroadcastHelper.Broadcast();
    }
}