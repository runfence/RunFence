using System.Drawing.Drawing2D;

namespace RunFence.DragBridge;

/// <summary>
/// Renders the visual content of the DragBridge window onto a <see cref="Graphics"/> surface.
/// Green with a down-arrow = receive mode (no files). Blue with file/folder icon = send mode (has files).
/// Icon shape offsets are fixed pixel values (not DPI-scaled) — icons appear smaller at higher DPI
/// but remain centered relative to the DPI-scaled circle.
/// </summary>
public static class DragBridgeIconRenderer
{
    private enum IconKind
    {
        SingleFile,
        MultiFile,
        SingleFolder,
        MultiFolder
    }

    /// <summary>
    /// Paints the DragBridge icon into <paramref name="bounds"/> using <paramref name="g"/>.
    /// <paramref name="filePaths"/> null or empty = receive mode (green arrow);
    /// non-empty = send mode (blue file/folder icon).
    /// </summary>
    public static void Paint(Graphics g, Rectangle bounds, List<string>? filePaths)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int cx = bounds.Width / 2, cy = bounds.Height / 2;

        if (filePaths is { Count: > 0 })
        {
            using var brush = new SolidBrush(Color.FromArgb(0, 100, 200));
            g.FillEllipse(brush, 2, 2, bounds.Width - 4, bounds.Height - 4);

            using var iconBrush = new SolidBrush(Color.White);
            DrawContentIcon(g, iconBrush, cx, cy, DetermineIconKind(filePaths));
        }
        else
        {
            using var brush = new SolidBrush(Color.FromArgb(0, 160, 80));
            g.FillEllipse(brush, 2, 2, bounds.Width - 4, bounds.Height - 4);

            using var arrowBrush = new SolidBrush(Color.White);
            var arrowPts = new[]
            {
                new Point(cx, cy + 14),
                new Point(cx - 10, cy),
                new Point(cx - 4, cy),
                new Point(cx - 4, cy - 14),
                new Point(cx + 4, cy - 14),
                new Point(cx + 4, cy),
                new Point(cx + 10, cy)
            };
            g.FillPolygon(arrowBrush, arrowPts);
        }
    }

    private static void DrawContentIcon(Graphics g, SolidBrush brush, int cx, int cy, IconKind kind)
    {
        switch (kind)
        {
            case IconKind.MultiFile:
                DrawPageShape(g, brush, cx + 4, cy - 4);
                DrawPageShape(g, brush, cx, cy);
                break;
            case IconKind.SingleFolder:
                DrawFolderShape(g, brush, cx, cy);
                break;
            case IconKind.MultiFolder:
                DrawFolderShape(g, brush, cx + 4, cy - 3);
                DrawFolderShape(g, brush, cx, cy);
                break;
            default: // SingleFile
                DrawPageShape(g, brush, cx, cy);
                break;
        }
    }

    private static void DrawPageShape(Graphics g, SolidBrush brush, float dx, float dy)
    {
        // Rectangle with folded top-right corner: L-shaped notch exposes blue background as the fold
        var pts = new PointF[]
        {
            new(dx - 7, dy - 12), // TL
            new(dx + 4, dy - 12), // fold start (top)
            new(dx + 4, dy - 7), // fold inner corner
            new(dx + 8, dy - 7), // fold outer corner
            new(dx + 8, dy + 12), // BR
            new(dx - 7, dy + 12), // BL
        };
        g.FillPolygon(brush, pts);
    }

    private static void DrawFolderShape(Graphics g, SolidBrush brush, float dx, float dy)
    {
        // Tab (top part of folder)
        g.FillRectangle(brush, dx - 9, dy - 13, 8, 4);
        // Body
        g.FillRectangle(brush, dx - 9, dy - 9, 18, 20);
    }

    private static IconKind DetermineIconKind(List<string> paths)
    {
        bool allFolders = paths.All(Directory.Exists);
        bool anyFolder = paths.Any(Directory.Exists);
        return (anyFolder, allFolders, paths.Count > 1) switch
        {
            (false, _, false) => IconKind.SingleFile,
            (false, _, true) => IconKind.MultiFile,
            (true, true, false) => IconKind.SingleFolder,
            (true, true, true) => IconKind.MultiFolder,
            _ => IconKind.MultiFile,  // mixed: some folders, some non-folders
        };
    }
}
