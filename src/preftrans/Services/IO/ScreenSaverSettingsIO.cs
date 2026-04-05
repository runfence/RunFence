using Microsoft.Win32;
using PrefTrans.Native;
using PrefTrans.Settings;

namespace PrefTrans.Services.IO;

public static class ScreenSaverSettingsIO
{
    public static ScreenSaverSettings Read()
    {
        var screenSaver = new ScreenSaverSettings();
        SafeExecutor.Try(() =>
        {
            int val = 0;
            if (NativeMethods.SystemParametersInfo(Constants.SPI_GETSCREENSAVEACTIVE, 0, ref val, 0))
                screenSaver.Enabled = val != 0;
        }, "reading");
        SafeExecutor.Try(() =>
        {
            int val = 0;
            if (NativeMethods.SystemParametersInfo(Constants.SPI_GETSCREENSAVETIMEOUT, 0, ref val, 0))
                screenSaver.TimeoutSeconds = val;
        }, "reading");
        SafeExecutor.Try(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.RegDesktop);
            screenSaver.ExecutablePath = key?.GetValue("SCRNSAVE.EXE") as string;
            screenSaver.RequirePassword = key?.GetValue("ScreenSaverIsSecure") as string;
            if (key?.GetValue("DelayLockInterval") is int v)
                screenSaver.DelayLockInterval = v;
        }, "reading");
        return screenSaver;
    }

    public static void Write(ScreenSaverSettings screenSaver)
    {
        SafeExecutor.Try(() =>
        {
            if (screenSaver.Enabled.HasValue)
                NativeMethods.SystemParametersInfo(Constants.SPI_SETSCREENSAVEACTIVE, screenSaver.Enabled.Value ? 1u : 0u, IntPtr.Zero, Constants.SPIF_UPDATEANDNOTIFY);
        }, "writing");
        SafeExecutor.Try(() =>
        {
            if (screenSaver.TimeoutSeconds.HasValue)
                NativeMethods.SystemParametersInfo(Constants.SPI_SETSCREENSAVETIMEOUT, (uint)screenSaver.TimeoutSeconds.Value, IntPtr.Zero, Constants.SPIF_UPDATEANDNOTIFY);
        }, "writing");
        bool changed = false;
        SafeExecutor.Try(() =>
        {
            using var key = Registry.CurrentUser.CreateSubKey(Constants.RegDesktop);
            if (screenSaver.ExecutablePath != null)
            {
                key.SetValue("SCRNSAVE.EXE", screenSaver.ExecutablePath, RegistryValueKind.String);
                changed = true;
            }

            if (screenSaver.RequirePassword != null)
            {
                key.SetValue("ScreenSaverIsSecure", screenSaver.RequirePassword, RegistryValueKind.String);
                changed = true;
            }

            if (screenSaver.DelayLockInterval.HasValue)
            {
                key.SetValue("DelayLockInterval", screenSaver.DelayLockInterval.Value, RegistryValueKind.DWord);
                changed = true;
            }
        }, "writing");
        if (changed)
            BroadcastHelper.Broadcast();
    }
}