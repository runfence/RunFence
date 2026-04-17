using Microsoft.Win32;
using PrefTrans.Native;
using PrefTrans.Services;
using PrefTrans.Settings;

namespace PrefTrans.Services.IO;

public class DesktopSettingsIO(ISafeExecutor safe, IBroadcastHelper broadcast) : ISettingsIO
{
    public DesktopSettings Read()
    {
        var desktop = new DesktopSettings();
        safe.Try(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.RegDesktop);
            if (key == null)
                return;
            desktop.WallpaperPath = key.GetValue("Wallpaper") as string;
            desktop.CursorBlinkRate = key.GetValue("CursorBlinkRate") as string;

            var style = key.GetValue("WallpaperStyle") as string;
            var tile = key.GetValue("TileWallpaper") as string;
            desktop.WallpaperStyle = (style, tile) switch
            {
                ("0", "1") => "Tile",
                ("0", _) => "Center",
                ("2", _) => "Stretch",
                ("6", _) => "Fit",
                ("10", _) => "Fill",
                ("22", _) => "Span",
                _ => style,
            };

            desktop.WaitToKillAppTimeout = key.GetValue("WaitToKillAppTimeout") as string;
            desktop.AutoEndTasks = key.GetValue("AutoEndTasks") as string;
            desktop.HungAppTimeout = key.GetValue("HungAppTimeout") as string;
            desktop.LowLevelHooksTimeout = key.GetValue("LowLevelHooksTimeout") as string;
        }, "reading");
        safe.Try(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.RegDesktop);
            if (key?.GetValue("CaretWidth") is int v)
                desktop.CaretWidth = v;
        }, "reading");
        safe.Try(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(Constants.RegWallpapers);
            if (key?.GetValue("BackgroundType") is int v)
                desktop.BackgroundType = v;
        }, "reading");
        return desktop;
    }

    public void Write(DesktopSettings desktop)
    {
        bool changed = false;
        safe.Try(() =>
        {
            if (desktop.WallpaperStyle != null)
            {
                using var key = Registry.CurrentUser.CreateSubKey(Constants.RegDesktop);
                var (style, tile) = desktop.WallpaperStyle switch
                {
                    "Center" => ("0", "0"),
                    "Tile" => ("0", "1"),
                    "Stretch" => ("2", "0"),
                    "Fit" => ("6", "0"),
                    "Fill" => ("10", "0"),
                    "Span" => ("22", "0"),
                    _ => (desktop.WallpaperStyle, "0"),
                };
                key.SetValue("WallpaperStyle", style, RegistryValueKind.String);
                key.SetValue("TileWallpaper", tile, RegistryValueKind.String);
                changed = true;
            }
        }, "writing");
        safe.Try(() =>
        {
            if (desktop.WallpaperPath == null)
                return;
            if (desktop.WallpaperPath.StartsWith(@"\\"))
            {
                // SystemParametersInfo silently drops UNC paths — write directly to registry instead.
                using var key = Registry.CurrentUser.CreateSubKey(Constants.RegDesktop);
                key.SetValue("Wallpaper", desktop.WallpaperPath, RegistryValueKind.String);
                changed = true;
            }
            else
            {
                NativeMethods.SystemParametersInfo(Constants.SPI_SETDESKWALLPAPER, 0, desktop.WallpaperPath, Constants.SPIF_UPDATEANDNOTIFY);
            }
        }, "writing");
        safe.Try(() =>
        {
            if (desktop.CursorBlinkRate != null)
            {
                using var key = Registry.CurrentUser.CreateSubKey(Constants.RegDesktop);
                key.SetValue("CursorBlinkRate", desktop.CursorBlinkRate, RegistryValueKind.String);
                changed = true;
            }
        }, "writing");
        safe.Try(() =>
        {
            if (desktop.CaretWidth.HasValue)
            {
                using var key = Registry.CurrentUser.CreateSubKey(Constants.RegDesktop);
                key.SetValue("CaretWidth", desktop.CaretWidth.Value, RegistryValueKind.DWord);
                changed = true;
            }
        }, "writing");
        safe.Try(() =>
        {
            if (desktop.BackgroundType.HasValue)
            {
                using var key = Registry.CurrentUser.CreateSubKey(Constants.RegWallpapers);
                key.SetValue("BackgroundType", desktop.BackgroundType.Value, RegistryValueKind.DWord);
                changed = true;
            }
        }, "writing");
        safe.Try(() =>
        {
            using var key = Registry.CurrentUser.CreateSubKey(Constants.RegDesktop);
            if (desktop.WaitToKillAppTimeout != null)
            {
                key.SetValue("WaitToKillAppTimeout", desktop.WaitToKillAppTimeout, RegistryValueKind.String);
                changed = true;
            }

            if (desktop.AutoEndTasks != null)
            {
                key.SetValue("AutoEndTasks", desktop.AutoEndTasks, RegistryValueKind.String);
                changed = true;
            }

            if (desktop.HungAppTimeout != null)
            {
                key.SetValue("HungAppTimeout", desktop.HungAppTimeout, RegistryValueKind.String);
                changed = true;
            }

            if (desktop.LowLevelHooksTimeout != null)
            {
                key.SetValue("LowLevelHooksTimeout", desktop.LowLevelHooksTimeout, RegistryValueKind.String);
                changed = true;
            }
        }, "writing");
        if (changed)
            broadcast.Broadcast();
    }

    void ISettingsIO.ReadInto(UserSettings s) => s.Desktop = Read();

    void ISettingsIO.WriteFrom(UserSettings s) { if (s.Desktop != null) Write(s.Desktop); }
}