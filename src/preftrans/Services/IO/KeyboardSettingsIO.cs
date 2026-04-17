using Microsoft.Win32;
using PrefTrans.Native;
using PrefTrans.Services;
using PrefTrans.Settings;

namespace PrefTrans.Services.IO;

public class KeyboardSettingsIO(ISafeExecutor safe, IBroadcastHelper broadcast) : ISettingsIO
{
    public KeyboardSettings Read()
    {
        var keyboard = new KeyboardSettings();
        safe.Try(() =>
        {
            int val = 0;
            if (NativeMethods.SystemParametersInfo(Constants.SPI_GETKEYBOARDDELAY, 0, ref val, 0))
                keyboard.KeyboardDelay = val;
        }, "reading");
        safe.Try(() =>
        {
            int val = 0;
            if (NativeMethods.SystemParametersInfo(Constants.SPI_GETKEYBOARDSPEED, 0, ref val, 0))
                keyboard.KeyboardSpeed = val;
        }, "reading");
        safe.Try(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.RegKeyboard);
            keyboard.NumLockOnStartup = key?.GetValue("InitialKeyboardIndicators") as string;
        }, "reading");
        return keyboard;
    }

    public void Write(KeyboardSettings keyboard)
    {
        safe.Try(() =>
        {
            if (keyboard.KeyboardDelay.HasValue)
                NativeMethods.SystemParametersInfo(Constants.SPI_SETKEYBOARDDELAY, (uint)keyboard.KeyboardDelay.Value, IntPtr.Zero, Constants.SPIF_UPDATEANDNOTIFY);
        }, "writing");
        safe.Try(() =>
        {
            if (keyboard.KeyboardSpeed.HasValue)
                NativeMethods.SystemParametersInfo(Constants.SPI_SETKEYBOARDSPEED, (uint)keyboard.KeyboardSpeed.Value, IntPtr.Zero, Constants.SPIF_UPDATEANDNOTIFY);
        }, "writing");
        safe.Try(() =>
        {
            if (keyboard.NumLockOnStartup != null)
            {
                using var key = Registry.CurrentUser.CreateSubKey(Constants.RegKeyboard);
                key.SetValue("InitialKeyboardIndicators", keyboard.NumLockOnStartup, RegistryValueKind.String);
                broadcast.Broadcast();
            }
        }, "writing");
    }

    void ISettingsIO.ReadInto(UserSettings s) => s.Keyboard = Read();

    void ISettingsIO.WriteFrom(UserSettings s) { if (s.Keyboard != null) Write(s.Keyboard); }
}