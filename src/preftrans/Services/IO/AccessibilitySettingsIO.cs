using Microsoft.Win32;
using PrefTrans.Native;
using PrefTrans.Settings;

namespace PrefTrans.Services.IO;

public static class AccessibilitySettingsIO
{
    public static AccessibilitySettings Read()
    {
        var accessibility = new AccessibilitySettings();
        SafeExecutor.Try(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.RegAccessibility + @"\StickyKeys");
            accessibility.StickyKeysFlags = key?.GetValue("Flags") as string;
        }, "reading");
        SafeExecutor.Try(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.RegAccessibility + @"\Keyboard Response");
            accessibility.FilterKeysFlags = key?.GetValue("Flags") as string;
        }, "reading");
        SafeExecutor.Try(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.RegAccessibility + @"\ToggleKeys");
            accessibility.ToggleKeysFlags = key?.GetValue("Flags") as string;
        }, "reading");
        SafeExecutor.Try(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.RegAccessibility + @"\MouseKeys");
            accessibility.MouseKeysFlags = key?.GetValue("Flags") as string;
        }, "reading");
        return accessibility;
    }

    public static void Write(AccessibilitySettings accessibility)
    {
        bool changed = false;
        SafeExecutor.Try(() =>
        {
            if (accessibility.StickyKeysFlags != null)
            {
                using var key = Registry.CurrentUser.CreateSubKey(Constants.RegAccessibility + @"\StickyKeys");
                key.SetValue("Flags", accessibility.StickyKeysFlags, RegistryValueKind.String);
                changed = true;
            }
        }, "writing");
        SafeExecutor.Try(() =>
        {
            if (accessibility.FilterKeysFlags != null)
            {
                using var key = Registry.CurrentUser.CreateSubKey(Constants.RegAccessibility + @"\Keyboard Response");
                key.SetValue("Flags", accessibility.FilterKeysFlags, RegistryValueKind.String);
                changed = true;
            }
        }, "writing");
        SafeExecutor.Try(() =>
        {
            if (accessibility.ToggleKeysFlags != null)
            {
                using var key = Registry.CurrentUser.CreateSubKey(Constants.RegAccessibility + @"\ToggleKeys");
                key.SetValue("Flags", accessibility.ToggleKeysFlags, RegistryValueKind.String);
                changed = true;
            }
        }, "writing");
        SafeExecutor.Try(() =>
        {
            if (accessibility.MouseKeysFlags != null)
            {
                using var key = Registry.CurrentUser.CreateSubKey(Constants.RegAccessibility + @"\MouseKeys");
                key.SetValue("Flags", accessibility.MouseKeysFlags, RegistryValueKind.String);
                changed = true;
            }
        }, "writing");
        if (changed)
            BroadcastHelper.Broadcast();
    }
}