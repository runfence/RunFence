using System.Drawing.Drawing2D;

namespace RunFence.UI.Forms;

public sealed class ContextHelpOverlay : Control
{
    private Bitmap? _backgroundSnapshot;
    private IReadOnlyList<Rectangle> _highlights = [];
    private Rectangle? _hoverHighlight;

    public ContextHelpOverlay()
    {
        SetStyle(
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);

        BackColor = Color.Transparent;
        Cursor = Cursors.Help;
        TabStop = false;
    }

    public void SetBackgroundSnapshot(Bitmap? backgroundSnapshot)
    {
        _backgroundSnapshot?.Dispose();
        _backgroundSnapshot = backgroundSnapshot;
        Invalidate();
    }

    public void SetHighlights(IReadOnlyList<Rectangle> highlights, Rectangle? hoverHighlight)
    {
        _highlights = highlights;
        _hoverHighlight = hoverHighlight;
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent?.BackColor ?? SystemColors.Control);

        if (_backgroundSnapshot != null)
            e.Graphics.DrawImageUnscaled(_backgroundSnapshot, Point.Empty);

        using var normalBrush = new SolidBrush(Color.FromArgb(70, 125, 200));
        using var hoverBrush = new SolidBrush(Color.FromArgb(40, 120, 180, 255));
        using var hoverBorderBrush = new SolidBrush(Color.FromArgb(40, 120, 180));

        foreach (var highlight in _highlights)
            DrawInsideBorder(e.Graphics, normalBrush, highlight, thickness: 2);

        if (_hoverHighlight is Rectangle hoverRect)
        {
            FillInsideRectangle(e.Graphics, hoverBrush, hoverRect, inset: 2);
            DrawInsideBorder(e.Graphics, hoverBorderBrush, hoverRect, thickness: 2);
        }
    }

    private static void DrawInsideBorder(Graphics graphics, Brush brush, Rectangle rect, int thickness)
    {
        if (rect.Width <= 0 || rect.Height <= 0 || thickness <= 0)
            return;

        var clampedThickness = Math.Min(thickness, Math.Min(rect.Width, rect.Height) / 2);
        if (clampedThickness <= 0)
            return;

        graphics.FillRectangle(brush, rect.Left, rect.Top, rect.Width, clampedThickness);
        graphics.FillRectangle(brush, rect.Left, rect.Bottom - clampedThickness, rect.Width, clampedThickness);
        graphics.FillRectangle(brush, rect.Left, rect.Top + clampedThickness, clampedThickness, rect.Height - (clampedThickness * 2));
        graphics.FillRectangle(brush, rect.Right - clampedThickness, rect.Top + clampedThickness, clampedThickness, rect.Height - (clampedThickness * 2));
    }

    private static void FillInsideRectangle(Graphics graphics, Brush brush, Rectangle rect, int inset)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        var fillRect = Rectangle.Inflate(rect, -Math.Max(0, inset), -Math.Max(0, inset));
        if (fillRect.Width <= 0 || fillRect.Height <= 0)
            return;

        graphics.FillRectangle(brush, fillRect);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _highlights = [];
            _hoverHighlight = null;
            _backgroundSnapshot?.Dispose();
            _backgroundSnapshot = null;
        }

        base.Dispose(disposing);
    }
}
