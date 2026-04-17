using Microsoft.Win32;
using PrefTrans.Native;
using PrefTrans.Services;
using PrefTrans.Settings;

namespace PrefTrans.Services.IO;

public class ScrollSettingsIO(ISafeExecutor safe, IBroadcastHelper broadcast) : ISettingsIO
{
    public ScrollSettings Read()
    {
        var scroll = new ScrollSettings();
        safe.Try(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.RegDesktop);
            scroll.WheelScrollLines = key?.GetValue("WheelScrollLines") as string;
            scroll.WheelScrollChars = key?.GetValue("WheelScrollChars") as string;
        }, "reading");
        return scroll;
    }

    public void Write(ScrollSettings scroll)
    {
        bool changed = false;
        safe.Try(() =>
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
            broadcast.Broadcast();
    }

    void ISettingsIO.ReadInto(UserSettings s) => s.Scroll = Read();

    void ISettingsIO.WriteFrom(UserSettings s) { if (s.Scroll != null) Write(s.Scroll); }
}