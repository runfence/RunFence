using System.Drawing.Drawing2D;

namespace RunFence.TrayIcon;

public sealed class TrayIconOverlayRenderer
{
    private const int MinimumDiameter = 8;

    public Icon CreateOverlayIcon(Icon baseIcon, Color markerColor)
    {
        ArgumentNullException.ThrowIfNull(baseIcon);

        using var baseBitmap = baseIcon.ToBitmap();
        using var workBitmap = new Bitmap(baseBitmap);
        using (var graphics = Graphics.FromImage(workBitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var badgeRect = CalculateBadgeBounds(baseBitmap.Size);

            using var badgeBrush = new SolidBrush(markerColor);
            graphics.FillEllipse(badgeBrush, badgeRect);

            var outlineColor = Color.FromArgb(230, Color.White);
            using var outlinePen = new Pen(outlineColor, 2f);
            var outlineRect = new Rectangle(
                Math.Max(0, badgeRect.X - 1),
                Math.Max(0, badgeRect.Y - 1),
                Math.Min(badgeRect.Width + 2, baseBitmap.Width - Math.Max(0, badgeRect.X - 1)),
                Math.Min(badgeRect.Height + 2, baseBitmap.Height - Math.Max(0, badgeRect.Y - 1)));
            graphics.DrawEllipse(outlinePen, outlineRect);
        }

        IntPtr hIcon = workBitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(hIcon).Clone();
        }
        finally
        {
            TrayIconOverlayNative.DestroyIcon(hIcon);
        }
    }

    public static Rectangle CalculateBadgeBounds(Size iconSize)
    {
        var shortestSide = Math.Min(iconSize.Width, iconSize.Height);
        var diameter = Math.Max(MinimumDiameter, shortestSide * 27 / 64);
        diameter = Math.Min(diameter, shortestSide);

        return new Rectangle(
            Math.Max(0, (iconSize.Width - diameter) / 2),
            Math.Max(0, (iconSize.Height - diameter) / 2),
            diameter,
            diameter);
    }
}
