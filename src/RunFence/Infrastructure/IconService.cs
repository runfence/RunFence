using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Infrastructure;

public class IconService(ILoggingService log, string? iconDir = null) : IIconService
{
    private readonly string _iconDir = iconDir ?? Constants.ProgramDataIconDir;

    // Badge colors for different accounts
    private static readonly Color[] BadgeColors =
    [
        Color.FromArgb(0x33, 0x99, 0xFF), // Blue
        Color.FromArgb(0xFF, 0x66, 0x33), // Orange
        Color.FromArgb(0x33, 0xCC, 0x66), // Green
        Color.FromArgb(0xCC, 0x33, 0x99), // Purple
        Color.FromArgb(0xFF, 0xCC, 0x00), // Yellow
        Color.FromArgb(0x00, 0xCC, 0xCC) // Teal
    ];

    public string CreateBadgedIcon(AppEntry app, string? customIconPath = null)
    {
        var outputPath = GetIconPath(app.Id);
        EnsureIconDirectory();

        try
        {
            Icon? sourceIcon = null;

            if (customIconPath != null && File.Exists(customIconPath))
            {
                sourceIcon = new Icon(customIconPath);
            }
            else if (app.IsFolder && Directory.Exists(app.ExePath))
            {
                sourceIcon = ExtractFolderIcon();
            }
            else if (app is { IsUrlScheme: false, IsFolder: false } && File.Exists(app.ExePath))
            {
                sourceIcon = Icon.ExtractAssociatedIcon(app.ExePath);
            }

            if (sourceIcon == null)
            {
                log.Warn($"Could not extract icon for app {app.Name}");
                return string.Empty;
            }

            using (sourceIcon)
            {
                // Use AppContainerName as badge identifier for container apps (AccountSid is empty)
                var badgeId = !string.IsNullOrEmpty(app.AccountSid)
                    ? app.AccountSid
                    : app.AppContainerName ?? app.Id;
                SaveBadgedIcon(sourceIcon, outputPath, badgeId);
            }

            log.Info($"Created badged icon for {app.Name} at {outputPath}");
            return outputPath;
        }
        catch (Exception ex)
        {
            log.Error($"Failed to create badged icon for {app.Name}", ex);
            return string.Empty;
        }
    }

    public bool NeedsRegeneration(AppEntry app)
    {
        if (app.IsUrlScheme)
            return false;

        var iconPath = GetIconPath(app.Id);
        if (!File.Exists(iconPath))
            return true;

        if (app.IsFolder)
            return false;

        if (!File.Exists(app.ExePath))
            return false;

        var exeTimestamp = File.GetLastWriteTimeUtc(app.ExePath);
        return app.LastKnownExeTimestamp == null || app.LastKnownExeTimestamp != exeTimestamp;
    }

    public void DeleteIcon(string appId)
    {
        var iconPath = GetIconPath(appId);
        if (File.Exists(iconPath))
        {
            try
            {
                File.Delete(iconPath);
                log.Info($"Deleted icon {iconPath}");
            }
            catch (Exception ex)
            {
                log.Error($"Failed to delete icon {iconPath}", ex);
            }
        }
    }

    public Image? GetOriginalAppIcon(AppEntry app, int size = 16)
    {
        try
        {
            if (app.IsFolder && Directory.Exists(app.ExePath))
            {
                using var icon = ExtractFolderIcon();
                if (icon != null)
                    return ScaleIconToBitmap(icon, size);
            }
            else if (app is { IsUrlScheme: false, IsFolder: false } && File.Exists(app.ExePath))
            {
                using var icon = Icon.ExtractAssociatedIcon(app.ExePath);
                if (icon != null)
                    return ScaleIconToBitmap(icon, size);
            }
        }
        catch (Exception ex)
        {
            log.Error($"Failed to get original app icon for {app.Name}", ex);
        }

        return null;
    }

    private static Bitmap ScaleIconToBitmap(Icon icon, int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(icon.ToBitmap(), 0, 0, size, size);
        return bmp;
    }

    private string GetIconPath(string appId)
    {
        return Path.Combine(_iconDir, $"{appId}.ico");
    }

    private void EnsureIconDirectory()
    {
        if (!Directory.Exists(_iconDir))
            Directory.CreateDirectory(_iconDir);
    }

    private void SaveBadgedIcon(Icon sourceIcon, string outputPath, string accountSid)
    {
        var sizes = new[] { 16, 32, 48, 256 };
        using var icon = IconBuilder.CreateMultiSizeIcon(sizes, (g, size) =>
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            using var sourceBmp = sourceIcon.ToBitmap();
            g.DrawImage(sourceBmp, 0, 0, size, size);
            DrawBadge(g, size, accountSid);
        });
        using var ms = new MemoryStream();
        icon.Save(ms);
        File.WriteAllBytes(outputPath, ms.ToArray());
    }

    private static Icon? ExtractFolderIcon()
    {
        var hIcon = ShellNative.GetFileIconHandle(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            FileSecurityNative.FILE_ATTRIBUTE_DIRECTORY,
            ShellNative.SHGFI_ICON | ShellNative.SHGFI_LARGEICON | ShellNative.SHGFI_USEFILEATTRIBUTES);

        if (hIcon == IntPtr.Zero)
            return null;

        var icon = (Icon)Icon.FromHandle(hIcon).Clone();
        WindowNative.DestroyIcon(hIcon);
        return icon;
    }

    private static void DrawBadge(Graphics g, int size, string accountSid)
    {
        var colorIndex = Math.Abs(SidNameResolver.DeterministicHash(accountSid)) % BadgeColors.Length;
        var badgeColor = BadgeColors[colorIndex];

        if (size <= 16)
        {
            // Small: just a colored dot
            const int dotSize = 6;
            var x = size - dotSize;
            var y = size - dotSize;

            using var brush = new SolidBrush(badgeColor);
            g.FillEllipse(brush, x, y, dotSize, dotSize);

            using var pen = new Pen(Color.White, 1);
            g.DrawEllipse(pen, x, y, dotSize, dotSize);
        }
        else
        {
            // Larger: colored circle with user silhouette
            var badgeSize = (int)(size * 0.35f);
            var x = size - badgeSize;
            var y = size - badgeSize;

            // Background circle
            using var brush = new SolidBrush(badgeColor);
            g.FillEllipse(brush, x, y, badgeSize, badgeSize);

            // Border
            using var pen = new Pen(Color.White, size >= 48 ? 2 : 1);
            g.DrawEllipse(pen, x, y, badgeSize, badgeSize);

            // Simple user silhouette (head + body)
            var cx = x + badgeSize / 2;
            var cy = y + badgeSize / 2;
            var headR = badgeSize / 6;

            using var whiteBrush = new SolidBrush(Color.White);
            // Head
            g.FillEllipse(whiteBrush, cx - headR, cy - headR - headR / 2, headR * 2, headR * 2);
            // Body (arc/half ellipse)
            var bodyW = (int)(badgeSize * 0.5f);
            var bodyH = (int)(badgeSize * 0.3f);
            g.FillEllipse(whiteBrush, cx - bodyW / 2, cy + headR / 2, bodyW, bodyH);
        }
    }
}