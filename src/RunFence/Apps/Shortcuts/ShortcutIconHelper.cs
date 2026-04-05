using System.Drawing.Drawing2D;
using RunFence.Core;

namespace RunFence.Apps.Shortcuts;

public static class ShortcutIconHelper
{
    public static Image? ExtractIcon(string exePath, int size = 16, ILoggingService? log = null)
    {
        if (!File.Exists(exePath))
        {
            log?.Warn($"Icon skipped — file not found: {exePath}");
            return null;
        }

        try
        {
            using var rawIcon = Icon.ExtractAssociatedIcon(exePath);
            if (rawIcon == null)
            {
                log?.Warn($"Icon extraction returned null: {exePath}");
                return null;
            }

            var bmp = new Bitmap(size, size);
            using var g = Graphics.FromImage(bmp);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            using var iconBmp = rawIcon.ToBitmap();
            g.DrawImage(iconBmp, 0, 0, size, size);
            return bmp;
        }
        catch (Exception ex)
        {
            log?.Error($"Icon extraction failed for {exePath}", ex);
            return null;
        }
    }
}