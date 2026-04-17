using Microsoft.Win32;
using PrefTrans.Native;
using PrefTrans.Services;
using PrefTrans.Settings;

namespace PrefTrans.Services.IO;

public class ScreenSaverSettingsIO(ISafeExecutor safe, IBroadcastHelper broadcast) : ISettingsIO
{
    public ScreenSaverSettings Read()
    {
        var screenSaver = new ScreenSaverSettings();
        safe.Try(() =>
        {
            int val = 0;
            if (NativeMethods.SystemParametersInfo(Constants.SPI_GETSCREENSAVEACTIVE, 0, ref val, 0))
                screenSaver.Enabled = val != 0;
        }, "reading");
        safe.Try(() =>
        {
            int val = 0;
            if (NativeMethods.SystemParametersInfo(Constants.SPI_GETSCREENSAVETIMEOUT, 0, ref val, 0))
                screenSaver.TimeoutSeconds = val;
        }, "reading");
        safe.Try(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.RegDesktop);
            screenSaver.ExecutablePath = key?.GetValue("SCRNSAVE.EXE") as string;
            screenSaver.RequirePassword = key?.GetValue("ScreenSaverIsSecure") as string;
            if (key?.GetValue("DelayLockInterval") is int v)
                screenSaver.DelayLockInterval = v;
        }, "reading");
        return screenSaver;
    }

    public void Write(ScreenSaverSettings screenSaver)
    {
        safe.Try(() =>
        {
            if (screenSaver.Enabled.HasValue)
                NativeMethods.SystemParametersInfo(Constants.SPI_SETSCREENSAVEACTIVE, screenSaver.Enabled.Value ? 1u : 0u, IntPtr.Zero, Constants.SPIF_UPDATEANDNOTIFY);
        }, "writing");
        safe.Try(() =>
        {
            if (screenSaver.TimeoutSeconds.HasValue)
                NativeMethods.SystemParametersInfo(Constants.SPI_SETSCREENSAVETIMEOUT, (uint)screenSaver.TimeoutSeconds.Value, IntPtr.Zero, Constants.SPIF_UPDATEANDNOTIFY);
        }, "writing");
        bool changed = false;
        safe.Try(() =>
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
            broadcast.Broadcast();
    }

    void ISettingsIO.ReadInto(UserSettings s) => s.ScreenSaver = Read();

    void ISettingsIO.WriteFrom(UserSettings s) { if (s.ScreenSaver != null) Write(s.ScreenSaver); }
}