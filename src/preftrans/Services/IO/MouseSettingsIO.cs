using Microsoft.Win32;
using PrefTrans.Native;
using PrefTrans.Services;
using PrefTrans.Settings;

namespace PrefTrans.Services.IO;

public class MouseSettingsIO(ISafeExecutor safe, IBroadcastHelper broadcast) : ISettingsIO
{
    public MouseSettings Read()
    {
        var mouse = new MouseSettings();
        safe.Try(() =>
        {
            int speed = 0;
            if (NativeMethods.SystemParametersInfo(Constants.SPI_GETMOUSESPEED, 0, ref speed, 0))
                mouse.MouseSpeed = speed;
        }, "reading");
        safe.Try(() => mouse.ButtonsSwapped = NativeMethods.GetSystemMetrics(Constants.SM_SWAPBUTTON) != 0, "reading");
        safe.Try(() => mouse.DoubleClickTime = NativeMethods.GetDoubleClickTime(), "reading");
        safe.Try(() =>
        {
            int[] mouseParams = new int[3];
            if (NativeMethods.SystemParametersInfo(Constants.SPI_GETMOUSE, 0, mouseParams, 0))
            {
                mouse.MouseThreshold1 = mouseParams[0].ToString();
                mouse.MouseThreshold2 = mouseParams[1].ToString();
                mouse.MouseAccelSpeed = mouseParams[2].ToString();
            }
        }, "reading");
        safe.Try(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.RegCursors);
            if (key?.GetValue("CursorBaseSize") is int v)
                mouse.CursorBaseSize = v;
        }, "reading");
        return mouse;
    }

    public void Write(MouseSettings mouse)
    {
        safe.Try(() =>
        {
            if (mouse.MouseSpeed.HasValue)
                NativeMethods.SystemParametersInfo(Constants.SPI_SETMOUSESPEED, 0, mouse.MouseSpeed.Value, Constants.SPIF_UPDATEANDNOTIFY);
        }, "writing");
        safe.Try(() =>
        {
            if (mouse.ButtonsSwapped.HasValue)
            {
                NativeMethods.SwapMouseButton(mouse.ButtonsSwapped.Value);
                using var key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Mouse");
                key.SetValue("SwapMouseButtons", mouse.ButtonsSwapped.Value ? "1" : "0", RegistryValueKind.String);
            }
        }, "writing");
        safe.Try(() =>
        {
            if (mouse.DoubleClickTime.HasValue)
                NativeMethods.SystemParametersInfo(Constants.SPI_SETDOUBLECLICKTIME, mouse.DoubleClickTime.Value, IntPtr.Zero, Constants.SPIF_UPDATEANDNOTIFY);
        }, "writing");
        safe.Try(() =>
        {
            if (mouse is { MouseThreshold1: not null, MouseThreshold2: not null, MouseAccelSpeed: not null })
            {
                int[] mouseParams =
                [
                    int.Parse(mouse.MouseThreshold1),
                    int.Parse(mouse.MouseThreshold2),
                    int.Parse(mouse.MouseAccelSpeed)
                ];
                NativeMethods.SystemParametersInfo(Constants.SPI_SETMOUSE, 0, mouseParams, Constants.SPIF_UPDATEANDNOTIFY);
            }
        }, "writing");
        safe.Try(() =>
        {
            if (mouse.CursorBaseSize.HasValue)
            {
                using var key = Registry.CurrentUser.CreateSubKey(Constants.RegCursors);
                key.SetValue("CursorBaseSize", mouse.CursorBaseSize.Value, RegistryValueKind.DWord);
                broadcast.Broadcast();
            }
        }, "writing");
    }

    void ISettingsIO.ReadInto(UserSettings s) => s.Mouse = Read();

    void ISettingsIO.WriteFrom(UserSettings s) { if (s.Mouse != null) Write(s.Mouse); }
}