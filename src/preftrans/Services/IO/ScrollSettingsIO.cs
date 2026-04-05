using Microsoft.Win32;
using PrefTrans.Native;
using PrefTrans.Settings;

namespace PrefTrans.Services.IO;

public static class ScrollSettingsIO
{
    public static ScrollSettings Read()
    {
        var scroll = new ScrollSettings();
        SafeExecutor.Try(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.RegDesktop);
            scroll.WheelScrollLines = key?.GetValue("WheelScrollLines") as string;
            scroll.WheelScrollChars = key?.GetValue("WheelScrollChars") as string;
        }, "reading");
        return scroll;
    }

    public static void Write(ScrollSettings scroll)
    {
        bool changed = false;
        SafeExecutor.Try(() =>
        {
            using var key = Registry.CurrentUser.CreateSubKey(Constants.RegDesktop);
            if (scroll.WheelScrollLines != null)
            {
                key.SetValue("WheelScrollLines", scroll.WheelScrollLines, RegistryValueKind.String);
                changed = true;
            }

            if (scroll.WheelScrollChars != null)
            {
                key.SetValue("WheelScrollChars", scroll.WheelScrollChars, RegistryValueKind.String);
                changed = true;
            }
        }, "writing");
        if (changed)
            BroadcastHelper.Broadcast();
    }
}