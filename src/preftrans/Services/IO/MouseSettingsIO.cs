using Microsoft.Win32;
using PrefTrans.Native;
using PrefTrans.Settings;

namespace PrefTrans.Services.IO;

public static class MouseSettingsIO
{
    public static MouseSettings Read()
    {
        var mouse = new MouseSettings();
        SafeExecutor.Try(() =>
        {
            int speed = 0;
            if (NativeMethods.SystemParametersInfo(Constants.SPI_GETMOUSESPEED, 0, ref speed, 0))
                mouse.MouseSpeed = speed;
        }, "reading");
        SafeExecutor.Try(() => mouse.ButtonsSwapped = NativeMethods.GetSystemMetrics(Constants.SM_SWAPBUTTON) != 0, "reading");
        SafeExecutor.Try(() => mouse.DoubleClickTime = NativeMethods.GetDoubleClickTime(), "reading");
        SafeExecutor.Try(() =>
        {
            int[] mouseParams = new int[3];
            if (NativeMethods.SystemParametersInfo(Constants.SPI_GETMOUSE, 0, mouseParams, 0))
            {
                mouse.MouseThreshold1 = mouseParams[0].ToString();
                mouse.MouseThreshold2 = mouseParams[1].ToString();
                mouse.MouseAccelSpeed = mouseParams[2].ToString();
            }
        }, "reading");
        SafeExecutor.Try(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.RegCursors);
            if (key?.GetValue("CursorBaseSize") is int v)
                mouse.CursorBaseSize = v;
        }, "reading");
        return mouse;
    }

    public static void Write(MouseSettings mouse)
    {
        SafeExecutor.Try(() =>
        {
            if (mouse.MouseSpeed.HasValue)
                NativeMethods.SystemParametersInfo(Constants.SPI_SETMOUSESPEED, 0, mouse.MouseSpeed.Value, Constants.SPIF_UPDATEANDNOTIFY);
        }, "writing");
        SafeExecutor.Try(() =>
        {
            if (mouse.ButtonsSwapped.HasValue)
            {
                NativeMethods.SwapMouseButton(mouse.ButtonsSwapped.Value);
                using var key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Mouse");
                key.SetValue("SwapMouseButtons", mouse.ButtonsSwapped.Value ? "1" : "0", RegistryValueKind.String);
            }
        }, "writing");
        SafeExecutor.Try(() =>
        {
            if (mouse.DoubleClickTime.HasValue)
                NativeMethods.SystemParametersInfo(Constants.SPI_SETDOUBLECLICKTIME, mouse.DoubleClickTime.Value, IntPtr.Zero, Constants.SPIF_UPDATEANDNOTIFY);
        }, "writing");
        SafeExecutor.Try(() =>
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
        SafeExecutor.Try(() =>
        {
            if (mouse.CursorBaseSize.HasValue)
            {
                using var key = Registry.CurrentUser.CreateSubKey(Constants.RegCursors);
                key.SetValue("CursorBaseSize", mouse.CursorBaseSize.Value, RegistryValueKind.DWord);
                BroadcastHelper.Broadcast();
            }
        }, "writing");
    }
}