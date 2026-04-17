using System.Drawing.Drawing2D;
using System.Drawing.Text;
using RunFence.Infrastructure;

namespace RunFence.UI;

/// <summary>
/// Static factory for creating UI icons (toolbar, dialog, context-menu) used across panels and forms.
/// All methods that previously lived as static members of <see cref="Forms.DataPanel"/> are here so
/// non-panel types (dialogs, context menu builders, etc.) can use them without depending on DataPanel.
/// </summary>
public static class UiIconFactory
{
    /// <summary>
    /// Creates a form/window Icon by rendering a Unicode symbol onto a bitmap.
    /// Caller is responsible for disposing the returned Icon.
    /// </summary>
    public static Icon CreateDialogIcon(string symbol, Color color, int size = 32)
    {
        using var bmp = (Bitmap)CreateToolbarIcon(symbol, color, size);
        var hIcon = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(hIcon).Clone();
        WindowNative.DestroyIcon(hIcon);
        return icon;
    }

    /// <summary>
    /// Creates a Windows-style document/properties icon for use in context menus.
    /// </summary>
    public static Image CreatePropertiesIcon(int size = 16)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.None;

        float s = size / 16f;
        int fold = Math.Max(1, (int)(3 * s));
        int px = (int)(2 * s), py = (int)(1 * s), pw = (int)(10 * s), ph = (int)(13 * s);

        using var borderPen = new Pen(Color.FromArgb(0x60, 0x60, 0x60), 1);
        using var pageBrush = new SolidBrush(Color.FromArgb(0xF8, 0xF8, 0xF8));
        using var linePen = new Pen(Color.FromArgb(0x90, 0x90, 0x90), 1);

        var page = new PointF[]
        {
            new(px, py),
            new(px + pw - fold, py),
            new(px + pw, py + fold),
            new(px + pw, py + ph),
            new(px, py + ph)
        };
        g.FillPolygon(pageBrush, page);
        g.DrawPolygon(borderPen, page);
        g.DrawLine(borderPen, px + pw - fold, py, px + pw - fold, py + fold);
        g.DrawLine(borderPen, px + pw - fold, py + fold, px + pw, py + fold);

        int lx1 = (int)(4 * s), lx2 = (int)(10 * s);
        g.DrawLine(linePen, lx1, (int)(6 * s), lx2, (int)(6 * s));
        g.DrawLine(linePen, lx1, (int)(8 * s), lx2, (int)(8 * s));
        g.DrawLine(linePen, lx1, (int)(10 * s), (int)(8 * s), (int)(10 * s));

        return bmp;
    }

    /// <summary>
    /// Creates a neutral Windows-style clipboard icon for use in context menus.
    /// </summary>
    public static Image CreateClipboardIcon(int size = 16)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.None;

        float s = size / 16f;
        int bx = (int)(2 * s), by = (int)(4 * s), bw = (int)(11 * s), bh = (int)(11 * s);
        int cx = (int)(5 * s), cy = (int)(2 * s), cw = (int)(5 * s), ch = (int)(4 * s);

        using var borderPen = new Pen(Color.FromArgb(0x6A, 0x6A, 0x6A), 1);
        using var bodyBrush = new SolidBrush(Color.FromArgb(0xF0, 0xED, 0xE4));
        using var clipBrush = new SolidBrush(Color.FromArgb(0xA0, 0xA0, 0xA0));
        using var linePen = new Pen(Color.FromArgb(0xB8, 0xB8, 0xB8), 1);

        g.FillRectangle(bodyBrush, bx, by, bw, bh);
        g.DrawRectangle(borderPen, bx, by, bw, bh);
        g.FillRectangle(clipBrush, cx, cy, cw, ch);
        g.DrawRectangle(borderPen, cx, cy, cw, ch);

        int lx1 = (int)(4 * s), lx2 = (int)(11 * s);
        g.DrawLine(linePen, lx1, (int)(8 * s), lx2, (int)(8 * s));
        g.DrawLine(linePen, lx1, (int)(10 * s), lx2, (int)(10 * s));
        g.DrawLine(linePen, lx1, (int)(12 * s), (int)(9 * s), (int)(12 * s));

        return bmp;
    }

    /// <summary>
    /// Creates a toolbar icon by rendering a Unicode symbol onto a bitmap.
    /// </summary>
    public static Image CreateToolbarIcon(string symbol, Color color, int size = 28)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var isFullSize = symbol is "+" or "\u2212";
        var fontSize = isFullSize ? size * 1.0f : size * 0.75f;
        using var font = new Font("Segoe UI Symbol", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(color);
        using var fmt = new StringFormat();
        fmt.Alignment = StringAlignment.Center;
        fmt.LineAlignment = StringAlignment.Center;

        var rect = new RectangleF(0, 0, size, size);
        g.DrawString(symbol, font, brush, rect, fmt);

        return bmp;
    }
}