using Microsoft.Win32;
using PrefTrans.Native;
using PrefTrans.Services;
using PrefTrans.Settings;

namespace PrefTrans.Services.IO;

public class AccessibilitySettingsIO(ISafeExecutor safe, IBroadcastHelper broadcast) : ISettingsIO
{
    public AccessibilitySettings Read()
    {
        var accessibility = new AccessibilitySettings();
        safe.Try(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.RegAccessibility + @"\StickyKeys");
            accessibility.StickyKeysFlags = key?.GetValue("Flags") as string;
        }, "reading");
        safe.Try(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.RegAccessibility + @"\Keyboard Response");
            accessibility.FilterKeysFlags = key?.GetValue("Flags") as string;
        }, "reading");
        safe.Try(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.RegAccessibility + @"\ToggleKeys");
            accessibility.ToggleKeysFlags = key?.GetValue("Flags") as string;
        }, "reading");
        safe.Try(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.RegAccessibility + @"\MouseKeys");
            accessibility.MouseKeysFlags = key?.GetValue("Flags") as string;
        }, "reading");
        return accessibility;
    }

    public void Write(AccessibilitySettings accessibility)
    {
        bool changed = false;
        safe.Try(() =>
        {
            if (accessibility.StickyKeysFlags != null)
            {
                using var key = Registry.CurrentUser.CreateSubKey(Constants.RegAccessibility + @"\StickyKeys");
                key.SetValue("Flags", accessibility.StickyKeysFlags, RegistryValueKind.String);
                changed = true;
            }
        }, "writing");
        safe.Try(() =>
        {
            if (accessibility.FilterKeysFlags != null)
            {
                using var key = Registry.CurrentUser.CreateSubKey(Constants.RegAccessibility + @"\Keyboard Response");
                key.SetValue("Flags", accessibility.FilterKeysFlags, RegistryValueKind.String);
                changed = true;
            }
        }, "writing");
        safe.Try(() =>
        {
            if (accessibility.ToggleKeysFlags != null)
            {
                using var key = Registry.CurrentUser.CreateSubKey(Constants.RegAccessibility + @"\ToggleKeys");
                key.SetValue("Flags", accessibility.ToggleKeysFlags, RegistryValueKind.String);
                changed = true;
            }
        }, "writing");
        safe.Try(() =>
        {
            if (accessibility.MouseKeysFlags != null)
            {
                using var key = Registry.CurrentUser.CreateSubKey(Constants.RegAccessibility + @"\MouseKeys");
                key.SetValue("Flags", accessibility.MouseKeysFlags, RegistryValueKind.String);
                changed = true;
            }
        }, "writing");
        if (changed)
            broadcast.Broadcast();
    }

    void ISettingsIO.ReadInto(UserSettings s) => s.Accessibility = Read();

    void ISettingsIO.WriteFrom(UserSettings s) { if (s.Accessibility != null) Write(s.Accessibility); }
}