using Microsoft.Win32;
using PrefTrans.Native;
using PrefTrans.Settings;

namespace PrefTrans.Services.IO;

public static class KeyboardSettingsIO
{
    public static KeyboardSettings Read()
    {
        var keyboard = new KeyboardSettings();
        SafeExecutor.Try(() =>
        {
            int val = 0;
            if (NativeMethods.SystemParametersInfo(Constants.SPI_GETKEYBOARDDELAY, 0, ref val, 0))
                keyboard.KeyboardDelay = val;
        }, "reading");
        SafeExecutor.Try(() =>
        {
            int val = 0;
            if (NativeMethods.SystemParametersInfo(Constants.SPI_GETKEYBOARDSPEED, 0, ref val, 0))
                keyboard.KeyboardSpeed = val;
        }, "reading");
        SafeExecutor.Try(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.RegKeyboard);
            keyboard.NumLockOnStartup = key?.GetValue("InitialKeyboardIndicators") as string;
        }, "reading");
        return keyboard;
    }

    public static void Write(KeyboardSettings keyboard)
    {
        SafeExecutor.Try(() =>
        {
            if (keyboard.KeyboardDelay.HasValue)
                NativeMethods.SystemParametersInfo(Constants.SPI_SETKEYBOARDDELAY, (uint)keyboard.KeyboardDelay.Value, IntPtr.Zero, Constants.SPIF_UPDATEANDNOTIFY);
        }, "writing");
        SafeExecutor.Try(() =>
        {
            if (keyboard.KeyboardSpeed.HasValue)
                NativeMethods.SystemParametersInfo(Constants.SPI_SETKEYBOARDSPEED, (uint)keyboard.KeyboardSpeed.Value, IntPtr.Zero, Constants.SPIF_UPDATEANDNOTIFY);
        }, "writing");
        SafeExecutor.Try(() =>
        {
            if (keyboard.NumLockOnStartup != null)
            {
                using var key = Registry.CurrentUser.CreateSubKey(Constants.RegKeyboard);
                key.SetValue("InitialKeyboardIndicators", keyboard.NumLockOnStartup, RegistryValueKind.String);
                BroadcastHelper.Broadcast();
            }
        }, "writing");
    }
}