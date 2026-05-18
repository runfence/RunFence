using System.Drawing.Text;
using System.Drawing.Drawing2D;

namespace RunFence.UI.Forms;

public class ContextHelpButton : Control
{
    private bool _hovered;
    private bool _pressed;

    public ContextHelpButton()
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
        AccessibleRole = AccessibleRole.PushButton;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _pressed = true;
            Invalidate();
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (_pressed)
        {
            _pressed = false;
            Invalidate();
        }

        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        if (_hovered || _pressed)
        {
            using var backgroundBrush = new SolidBrush(_pressed
                ? Color.FromArgb(212, 212, 212)
                : Color.FromArgb(226, 226, 226));
            e.Graphics.FillRectangle(backgroundBrush, ClientRectangle);
        }

        using var glyphBrush = new SolidBrush(_pressed ? Color.FromArgb(40, 40, 40) : Color.FromArgb(70, 70, 70));
        using var stringFormat = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        using var glyphPath = new GraphicsPath();
        var glyphSize = Math.Max(16f, ClientRectangle.Height * 0.72f);
        glyphPath.AddString(
            "?",
            Font.FontFamily,
            (int)FontStyle.Bold,
            glyphSize,
            Point.Empty,
            stringFormat);

        var glyphBounds = glyphPath.GetBounds();
        var centerX = ClientRectangle.Left + (ClientRectangle.Width / 2f);
        var centerY = ClientRectangle.Top + (ClientRectangle.Height / 2f);
        glyphPath.Transform(new Matrix(
            1,
            0,
            0,
            1,
            centerX - (glyphBounds.Left + (glyphBounds.Width / 2f)),
            centerY - (glyphBounds.Top + (glyphBounds.Height / 2f))));

        e.Graphics.FillPath(glyphBrush, glyphPath);
    }
}
